using System.Collections.ObjectModel;
using System.Security.Cryptography;
using StreamGuard.Models;

namespace StreamGuard.Services;

public enum RecoveryValidation { Valid, Closed, Unverified }
public sealed record RecoveryCheck(RecoveryValidation State, string Detail = "");
public sealed record RecoveryOutcome(bool WindowReleased, bool Saved);
public interface IRecoveryWindowApi
{
    bool TryCapture(IntPtr window, out RecoverySnapshot snapshot, out string error);
    bool TryTag(RecoveryRecord record, out string error);
    void RemoveTag(RecoveryRecord record);
    RecoveryCheck Validate(RecoveryRecord record);
    bool TryRestore(RecoveryRecord record, out string error);
}

public sealed class RecoveryService
{
    private readonly IRecoveryJournalStore _store;
    private readonly IRecoveryWindowApi _windows;
    private readonly ObservableCollection<RecoveryRecord> _records = new();
    private readonly Guid _instance = Guid.NewGuid();
    private readonly object _saveLock = new();
    private bool _loadFailed;
    public ReadOnlyObservableCollection<RecoveryRecord> Records { get; }
    public event Action? Changed;
    public string? Warning { get; private set; }
    public int PendingCount => _records.Count(record => record.CanRetry);
    public RecoveryService(IRecoveryJournalStore store, IRecoveryWindowApi windows)
    {
        _store = store; _windows = windows;
        Records = new(_records);
        Load();
    }
    private void Load()
    {
        try
        {
            var records = _store.Load();
            _records.Clear();
            foreach (var record in records)
            {
                record.IsPreviousSession = record.OwnerInstance != _instance;
                if (record.IsPreviousSession && record.State == RecoveryState.Protected) record.State = RecoveryState.Pending;
                _records.Add(record);
            }
            _loadFailed = false; Warning = _store.Warning;
        }
        catch (Exception error) { _loadFailed = true; Warning = "Recovery data could not be read: " + error.Message; }
    }
    private bool Save()
    {
        lock (_saveLock)
        {
            try { _store.Save(_records); Warning = _store.Warning; return true; }
            catch (Exception error) { Warning = "Recovery data could not be saved: " + error.Message; return false; }
            finally { Changed?.Invoke(); }
        }
    }
    public bool TryPrepare(IntPtr window, string applicationName, out RecoveryRecord? record)
    {
        record = null;
        if (_loadFailed) { Changed?.Invoke(); return false; }
        if (_records.Any(item => item.CanRetry && item.Snapshot.Identity.Window == window.ToInt64() && _windows.Validate(item).State == RecoveryValidation.Valid))
        { Warning = "This window already has pending recovery information. Restore it before protecting it again."; Changed?.Invoke(); return false; }
        if (!_windows.TryCapture(window, out var snapshot, out var error))
        { Warning = error; Changed?.Invoke(); return false; }
        var candidate = new RecoveryRecord
        {
            Snapshot = snapshot, ApplicationName = applicationName, OwnerInstance = _instance,
            Token = RandomNumberGenerator.GetInt32(1, int.MaxValue)
        };
        if (!_windows.TryTag(candidate, out error)) { Warning = error; Changed?.Invoke(); return false; }
        var history = _records.Where(item => !item.CanRetry).OrderByDescending(item => item.CreatedUtc).Skip(199).ToList();
        foreach (var old in history) _records.Remove(old);
        _records.Insert(0, candidate);
        if (!Save())
        {
            _records.Remove(candidate);
            _windows.RemoveTag(candidate);
            Changed?.Invoke();
            return false;
        }
        record = candidate;
        return true;
    }
    public void MarkProtected(Guid id, RecoveryMethod method)
    {
        var record = Find(id);
        if (record == null) return;
        record.Method = method; record.State = RecoveryState.Protected; record.Detail = "";
        // The earlier Pending snapshot remains sufficient if this status-only update cannot be saved.
        Save();
    }
    public RecoveryRecord? Find(Guid id) => _records.FirstOrDefault(record => record.Id == id);
    public bool Delete(Guid id)
    {
        var record = Find(id);
        if (record == null) return false;
        if (!record.CanDelete) return false;
        _windows.RemoveTag(record);
        _records.Remove(record);
        return Save();
    }
    public int ClearFinished()
    {
        var finished = _records.Where(record => !record.CanRetry).ToList();
        if (finished.Count == 0) return 0;
        foreach (var record in finished)
        {
            _windows.RemoveTag(record);
            _records.Remove(record);
        }
        Save();
        return finished.Count;
    }
    public RecoveryOutcome Restore(Guid id)
    {
        var record = Find(id);
        if (record == null) return new(false, false);
        if (!record.CanRetry) return new(true, true);
        try { return RestoreCore(record); }
        catch (Exception error)
        {
            record.State = RecoveryState.Failed; record.Detail = "Recovery failed: " + error.Message;
            return new(false, Save());
        }
    }
    private RecoveryOutcome RestoreCore(RecoveryRecord record)
    {
        if (record.WindowReleasedInMemory) return CompleteRestoration(record);
        var check = _windows.Validate(record);
        if (check.State == RecoveryValidation.Unverified)
        {
            record.State = RecoveryState.Unverified; record.Detail = check.Detail;
            return new(false, Save());
        }
        if (check.State == RecoveryValidation.Closed)
        {
            record.State = RecoveryState.Closed; record.Detail = "The original window no longer exists. Unsaved content cannot be recovered here.";
            return new(true, Save());
        }
        if (!_windows.TryRestore(record, out var error))
        {
            record.State = RecoveryState.Failed; record.Detail = error;
            return new(false, Save());
        }
        record.WindowReleasedInMemory = true;
        return CompleteRestoration(record);
    }
    private RecoveryOutcome CompleteRestoration(RecoveryRecord record)
    {
        record.State = RecoveryState.Recovered; record.Detail = "The window has been returned to its original desktop and layout.";
        if (Save()) { _windows.RemoveTag(record); return new(true, true); }
        record.State = RecoveryState.Failed;
        record.Detail = "The window was restored, but the result could not be saved. Retry after file access is restored.";
        Changed?.Invoke();
        return new(true, false);
    }
    public void Refresh()
    {
        if (_loadFailed) { Load(); Changed?.Invoke(); if (_loadFailed) return; }
        var changed = false;
        foreach (var record in _records.Where(record => record.CanRetry))
        {
            RecoveryCheck check;
            try { check = _windows.Validate(record); }
            catch { check = new(RecoveryValidation.Unverified, "The original process or window could not be verified. No action was taken."); }
            var state = check.State switch
            {
                RecoveryValidation.Closed => RecoveryState.Closed,
                RecoveryValidation.Unverified => RecoveryState.Unverified,
                _ => record.State == RecoveryState.Unverified ? RecoveryState.Pending : record.State
            };
            if (state != record.State) { record.State = state; record.Detail = check.Detail; changed = true; }
        }
        if (changed) Save(); else Changed?.Invoke();
    }
    public void Reset()
    {
        _records.Clear();
        _loadFailed = false;
        Warning = null;
        Changed?.Invoke();
    }
}

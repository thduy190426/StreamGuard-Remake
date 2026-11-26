using System;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace StreamGuard.Services;

/// <summary>
/// Prevents rapid repeated toggling of individual feature switches (CheckBox / ToggleButton)
/// within a configurable cooldown window.
/// </summary>
public sealed class SwitchCooldownService
{
    public const long DefaultCooldownMs = 1200;

    private sealed class CooldownEntry
    {
        public long LastTick;
    }

    private readonly ConditionalWeakTable<object, CooldownEntry> _lastToggleTicks = new();
    private readonly Func<long> _nowProvider;

    public long CooldownMs { get; }

    public SwitchCooldownService(long cooldownMs = DefaultCooldownMs, Func<long>? nowProvider = null)
    {
        if (cooldownMs <= 0)
            throw new ArgumentOutOfRangeException(nameof(cooldownMs), "Cooldown must be positive.");

        CooldownMs = cooldownMs;
        _nowProvider = nowProvider ?? (() => Environment.TickCount64);
    }

    public bool TryAcquireToggle(object switchKey, out double remainingSeconds)
    {
        if (switchKey == null)
            throw new ArgumentNullException(nameof(switchKey));

        var now = _nowProvider();
        if (_lastToggleTicks.TryGetValue(switchKey, out var entry))
        {
            var elapsed = now - entry.LastTick;
            if (elapsed >= 0 && elapsed < CooldownMs)
            {
                remainingSeconds = Math.Max(0.1, (CooldownMs - elapsed) / 1000.0);
                return false;
            }
            entry.LastTick = now;
        }
        else
        {
            _lastToggleTicks.Add(switchKey, new CooldownEntry { LastTick = now });
        }

        remainingSeconds = 0;
        return true;
    }

    public void Clear()
    {
        _lastToggleTicks.Clear();
    }

    public static ToggleButton? FindFeatureSwitch(DependencyObject? source)
    {
        var curr = source;
        while (curr != null)
        {
            if (curr is ToggleButton toggle)
            {
                if (!toggle.IsEnabled)
                    return null;

                // Ignore internal dropdown toggle buttons inside ComboBox templates.
                if (toggle.TemplatedParent is System.Windows.Controls.ComboBox ||
                    string.Equals(toggle.Name, "SelectorToggle", StringComparison.Ordinal))
                {
                    return null;
                }

                return toggle;
            }

            DependencyObject? parent = null;
            if (curr is Visual || curr is Visual3D)
            {
                parent = VisualTreeHelper.GetParent(curr);
            }
            curr = parent ?? LogicalTreeHelper.GetParent(curr);
        }

        return null;
    }
}

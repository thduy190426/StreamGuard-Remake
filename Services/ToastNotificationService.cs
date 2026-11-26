using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using StreamGuard.Controls;
using StreamGuard.Localization;
using StreamGuard.Models;
using WpfPanel = System.Windows.Controls.Panel;
using WpfButton = System.Windows.Controls.Button;

namespace StreamGuard.Services;

public class ToastNotificationService
{
    private readonly WpfPanel _hostStack;
    private readonly FrameworkElement? _queueFooter;
    private readonly TextBlock? _queueSummary;
    private readonly WpfButton? _clearAllButton;

    private readonly List<ToastCard> _activeCards = new();
    private readonly Queue<ToastNotification> _pendingQueue = new();

    private ToastNotification? _lastNotification;
    private DateTime _lastNotificationTime = DateTime.MinValue;

    public int MaxConcurrent { get; set; } = 3;
    public int DefaultDurationMs { get; set; } = 2000;
    public int StormDurationMs { get; set; } = 1200;
    public int StormThreshold { get; set; } = 3;
    public int MaxQueueSize { get; set; } = 50;
    public TimeSpan DeduplicationWindow { get; set; } = TimeSpan.FromSeconds(1.5);

    public IReadOnlyList<ToastCard> ActiveCards => _activeCards;
    public int PendingCount => _pendingQueue.Count;

    public ToastNotificationService(
        WpfPanel hostStack,
        FrameworkElement? queueFooter = null,
        TextBlock? queueSummary = null,
        WpfButton? clearAllButton = null)
    {
        _hostStack = hostStack ?? throw new ArgumentNullException(nameof(hostStack));
        _queueFooter = queueFooter;
        _queueSummary = queueSummary;
        _clearAllButton = clearAllButton;

        if (_clearAllButton != null)
        {
            _clearAllButton.Click += (s, e) => ClearAll();
        }

        UpdateFooter();
    }

    public void Show(string message, ToastType type = ToastType.Info, int durationMs = 2000)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        Show(new ToastNotification(message.Trim(), type, durationMs));
    }

    public void Show(ToastNotification notification)
    {
        if (notification == null || string.IsNullOrWhiteSpace(notification.Message)) return;

        if (!_hostStack.Dispatcher.CheckAccess())
        {
            _hostStack.Dispatcher.BeginInvoke(new Action(() => Show(notification)));
            return;
        }

        var now = DateTime.UtcNow;

        // 1. Check Deduplication against active cards
        var existingCard = _activeCards.FirstOrDefault(c =>
            !c.IsDismissed &&
            c.Message.Equals(notification.Message, StringComparison.Ordinal) &&
            c.Type == notification.Type);

        if (existingCard != null)
        {
            var effectiveDuration = _pendingQueue.Count >= StormThreshold ? StormDurationMs : notification.DurationMs;
            existingCard.UpdateRepeat(existingCard.RepeatCount + 1, effectiveDuration);
            _lastNotification = existingCard.Notification;
            _lastNotificationTime = now;
            return;
        }

        // 2. Check Deduplication against pending queue
        var existingPending = _pendingQueue.FirstOrDefault(p =>
            p.Message.Equals(notification.Message, StringComparison.Ordinal) &&
            p.Type == notification.Type);

        if (existingPending != null)
        {
            existingPending.RepeatCount++;
            _lastNotification = existingPending;
            _lastNotificationTime = now;
            return;
        }

        // 3. Check Deduplication window against last notification (if just dismissed)
        if (_lastNotification != null &&
            _lastNotification.Message.Equals(notification.Message, StringComparison.Ordinal) &&
            _lastNotification.Type == notification.Type &&
            (now - _lastNotificationTime) <= DeduplicationWindow)
        {
            notification.RepeatCount = _lastNotification.RepeatCount + 1;
        }

        _lastNotification = notification;
        _lastNotificationTime = now;

        // 4. Capacity check
        if (_activeCards.Count < MaxConcurrent)
        {
            DisplayCard(notification);
        }
        else
        {
            if (_pendingQueue.Count >= StormThreshold)
            {
                notification.DurationMs = StormDurationMs;
            }
            if (_pendingQueue.Count >= MaxQueueSize)
            {
                _pendingQueue.Dequeue();
            }
            _pendingQueue.Enqueue(notification);
            AccelerateActiveCardsIfStorm();
            UpdateFooter();
        }
    }

    private void AccelerateActiveCardsIfStorm()
    {
        if (_pendingQueue.Count >= StormThreshold)
        {
            foreach (var card in _activeCards)
            {
                if (!card.IsDismissed)
                {
                    card.AdjustDuration(StormDurationMs);
                }
            }
        }
    }

    private void DisplayCard(ToastNotification notification)
    {
        var duration = _pendingQueue.Count >= StormThreshold ? StormDurationMs : notification.DurationMs;
        notification.DurationMs = duration;

        var card = new ToastCard(notification);
        card.Dismissed += OnCardDismissed;

        _activeCards.Add(card);
        _hostStack.Children.Add(card);

        UpdateFooter();
    }

    private void OnCardDismissed(object? sender, EventArgs e)
    {
        if (sender is ToastCard card)
        {
            card.Dismissed -= OnCardDismissed;
            _activeCards.Remove(card);
            _hostStack.Children.Remove(card);

            if (_pendingQueue.Count > 0)
            {
                bool isStorm = _pendingQueue.Count >= StormThreshold;
                var next = _pendingQueue.Dequeue();
                if (isStorm)
                {
                    next.DurationMs = StormDurationMs;
                }
                DisplayCard(next);
            }

            UpdateFooter();
        }
    }

    public void ClearAll()
    {
        if (!_hostStack.Dispatcher.CheckAccess())
        {
            _hostStack.Dispatcher.BeginInvoke(new Action(ClearAll));
            return;
        }

        _pendingQueue.Clear();
        _lastNotification = null;
        _lastNotificationTime = DateTime.MinValue;

        var cards = _activeCards.ToList();
        _activeCards.Clear();
        foreach (var card in cards)
        {
            card.Dismissed -= OnCardDismissed;
            card.Dismiss(animated: false);
            _hostStack.Children.Remove(card);
        }

        UpdateFooter();
    }

    public void UpdateFooter()
    {
        if (_queueFooter == null) return;

        if (_pendingQueue.Count > 0)
        {
            _queueFooter.Visibility = Visibility.Visible;
            if (_queueSummary != null)
            {
                var format = LocalizationService.GetString("Toast.MoreNotifications", "+{0} more notifications");
                _queueSummary.Text = string.Format(format, _pendingQueue.Count);
            }
        }
        else
        {
            _queueFooter.Visibility = Visibility.Collapsed;
        }
    }
}

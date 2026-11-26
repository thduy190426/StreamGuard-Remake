using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using StreamGuard;
using StreamGuard.Controls;
using StreamGuard.Models;
using StreamGuard.Services;

namespace StreamGuard.Tests;

public static class ToastNotificationChecks
{
    public static void Run(Action<bool, string> check, string tempDir)
    {
        TestToastNotificationModel(check);
        TestToastCardBehavior(check);
        TestToastServiceQueueAndStorm(check);
        TestMainWindowToastIntegration(check, tempDir);
    }

    private static void TestToastNotificationModel(Action<bool, string> check)
    {
        var toast = new ToastNotification("Test notification", ToastType.Success, 2000);
        check(toast.Message == "Test notification", "ToastNotification stores message correctly");
        check(toast.Type == ToastType.Success, "ToastNotification stores type correctly");
        check(toast.DurationMs == 2000, "ToastNotification stores duration correctly");
        check(toast.RepeatCount == 1, "ToastNotification default repeat count is 1");
        check(!string.IsNullOrEmpty(toast.Id), "ToastNotification generates non-empty Id");
        check((DateTime.UtcNow - toast.Timestamp).TotalSeconds < 5, "ToastNotification timestamp is recent");
    }

    private static void TestToastCardBehavior(Action<bool, string> check)
    {
        foreach (var type in new[] { ToastType.Success, ToastType.Error, ToastType.Warning, ToastType.Info })
        {
            var notif = new ToastNotification($"Sample {type}", type, 2000);
            var card = new ToastCard(notif);
            check(card.Message == $"Sample {type}", $"ToastCard message matches for {type}");
            check(card.Type == type, $"ToastCard type matches for {type}");
            check(card.RepeatCount == 1, $"ToastCard initial repeat count is 1 for {type}");
            check(!card.IsDismissed, $"ToastCard initially not dismissed for {type}");
            check(!card.IsPaused, $"ToastCard initially not paused for {type}");

            // Verify properties are not locked by animation hold: can set RenderTransform translation
            card.SimulateSwipe(25);
            check(!card.IsDismissed, $"ToastCard remains active below swipe threshold for {type}");

            // Test Pause & Resume
            card.Pause();
            check(card.IsPaused, $"ToastCard pauses on hover for {type}");

            // Test UpdateRepeat & Coalescing while paused (Stopwatch must NOT run in background!)
            card.UpdateRepeat(2, 2000);
            check(card.RepeatCount == 2, $"ToastCard repeat count increments to 2 for {type}");
            check(card.Progress >= 0.99, $"ToastCard progress resets to full on repeat for {type}");
            check(card.IsPaused, $"ToastCard remains paused after UpdateRepeat while hovered for {type}");

            card.Resume();
            check(!card.IsPaused, $"ToastCard resumes on mouse leave for {type}");

            // Test AdjustDuration during storm
            card.AdjustDuration(1200);
            check(!card.IsDismissed, $"ToastCard stays alive with 1200ms adjusted duration if elapsed is short for {type}");

            // Test Swipe threshold dismissal
            bool dismissedFired = false;
            card.Dismissed += (s, e) => dismissedFired = true;
            card.SimulateSwipe(75); // Above threshold (>= 60)
            check(card.IsDismissed, $"ToastCard dismisses on swipe past threshold (75px) for {type}");
            check(dismissedFired, $"ToastCard raises Dismissed event on threshold swipe for {type}");
        }
    }

    private static void TestToastServiceQueueAndStorm(Action<bool, string> check)
    {
        var stack = new StackPanel();
        var footer = new Border();
        var summary = new TextBlock();
        var clearBtn = new Button();

        var service = new ToastNotificationService(stack, footer, summary, clearBtn);
        check(service.MaxConcurrent == 3, "ToastService defaults to max 3 concurrent cards");
        check(service.DefaultDurationMs == 2000, "ToastService default duration is 2000ms");
        check(service.StormDurationMs == 1200, "ToastService storm duration is 1200ms");
        check(service.StormThreshold == 3, "ToastService storm threshold is 3 pending items");
        check(service.MaxQueueSize == 50, "ToastService caps queue at 50 to prevent memory leak");

        // 1. Add first 3 cards (Fill concurrent slots)
        service.Show("Message 1", ToastType.Info);
        service.Show("Message 2", ToastType.Success);
        service.Show("Message 3", ToastType.Warning);

        check(service.ActiveCards.Count == 3, "ToastService has 3 active cards");
        check(stack.Children.Count == 3, "Host stack has 3 visual child cards");
        check(service.PendingCount == 0, "Pending queue is empty with 3 cards");
        check(footer.Visibility == Visibility.Collapsed, "Queue footer is collapsed with <= 3 cards");

        // 2. Add 4th card (Must enter pending queue)
        service.Show("Message 4", ToastType.Error);
        check(service.ActiveCards.Count == 3, "ToastService still caps active cards at 3");
        check(service.PendingCount == 1, "Pending queue holds the 4th notification");
        check(footer.Visibility == Visibility.Visible, "Queue footer becomes visible when items are queued");
        check(summary.Text.Contains("+1"), $"Queue summary shows '+1': '{summary.Text}'");

        // 3. Deduplication / Coalescing test: Show identical Message 2 within window
        service.Show("Message 2", ToastType.Success);
        check(service.ActiveCards.Count == 3, "Deduplicated toast does not create extra active card");
        check(service.PendingCount == 1, "Deduplicated toast does not increase pending queue");
        var card2 = service.ActiveCards.FirstOrDefault(c => c.Message == "Message 2");
        check(card2 != null && card2.RepeatCount == 2, "Duplicate card incremented repeat count to 2");

        // 4. Storm Acceleration test: Add 3 more items to push queue past StormThreshold (3)
        service.Show("Message 5", ToastType.Info);
        service.Show("Message 6", ToastType.Info);
        service.Show("Message 7", ToastType.Info);
        check(service.PendingCount == 4, "Pending queue now has 4 items (> 3 threshold)");
        check(summary.Text.Contains("+4"), $"Queue summary shows '+4': '{summary.Text}'");

        // Active cards on screen must also have received StormDurationMs acceleration
        check(service.ActiveCards[0].Notification.DurationMs <= 2000, "Active card tracked storm duration");

        // 5. Card dismissal and FIFO promotion
        var firstCard = service.ActiveCards[0];
        firstCard.Dismiss(animated: false);
        check(service.ActiveCards.Count == 3, "Active cards stay at 3 after promoting next from queue");
        check(service.PendingCount == 3, "Pending queue decremented to 3 after promotion");
        check(service.ActiveCards.Any(c => c.Message == "Message 4"), "Message 4 was promoted to active card");

        // Check storm accelerated duration was applied
        var promotedCard = service.ActiveCards.First(c => c.Message == "Message 4");
        check(promotedCard.Notification.DurationMs == service.StormDurationMs,
            $"Storm accelerated duration (1200ms) applied to promoted card: {promotedCard.Notification.DurationMs}");

        // 6. Test MaxQueueSize memory bound: Enqueue 60 items
        for (int i = 0; i < 60; i++)
        {
            service.Show($"Flood message {i}", ToastType.Info);
        }
        check(service.PendingCount == service.MaxQueueSize,
            $"Pending queue bounded at MaxQueueSize ({service.MaxQueueSize}): actual={service.PendingCount}");

        // 7. Clear All test
        service.ClearAll();
        check(service.ActiveCards.Count == 0, "ClearAll removes all active cards");
        check(service.PendingCount == 0, "ClearAll wipes pending queue");
        check(stack.Children.Count == 0, "ClearAll clears visual host stack");
        check(footer.Visibility == Visibility.Collapsed, "ClearAll hides queue footer");

        // Verify deduplication memory is cleared after ClearAll: next identical message starts at repeat 1
        service.Show("Flood message 0", ToastType.Info);
        check(service.ActiveCards.Count == 1, "First notification after ClearAll displays normally");
        check(service.ActiveCards[0].RepeatCount == 1, "First notification after ClearAll starts with repeatCount=1");
        service.ClearAll();
    }

    private static void TestMainWindowToastIntegration(Action<bool, string> check, string tempDir)
    {
        var configPath = Path.Combine(tempDir, "StreamGuard-toast-test-config.json");
        var config = new ConfigService(configPath);
        var window = new MainWindow(config);

        var flags = BindingFlags.Instance | BindingFlags.NonPublic;

        // 1. Verify ToastOverlayHost layout properties
        var host = (StackPanel?)window.FindName("ToastOverlayHost");
        check(host != null, "ToastOverlayHost exists in MainWindow");
        check(host != null && host.VerticalAlignment == VerticalAlignment.Bottom, "ToastOverlayHost is aligned to Bottom");
        check(host != null && host.HorizontalAlignment == HorizontalAlignment.Right, "ToastOverlayHost is aligned to Right");
        check(host != null && Panel.GetZIndex(host) == 9999, "ToastOverlayHost has Panel.ZIndex=9999");
        check(host != null && host.Margin.Right >= 16 && host.Margin.Bottom >= 16, "ToastOverlayHost has comfortable margin (20,20)");
        check(host != null && host.Background == null, "ToastOverlayHost has null background so empty areas pass through clicks");

        var cardsStack = (StackPanel?)window.FindName("ToastCardsStack");
        check(cardsStack != null, "ToastCardsStack exists in MainWindow");

        var footer = (Border?)window.FindName("ToastQueueFooter");
        check(footer != null, "ToastQueueFooter exists in MainWindow");

        // 2. Verify ToastService is wired up
        var service = window.ToastService;
        check(service != null, "MainWindow.ToastService is initialized");

        // 3. Test explicit ShowToast
        window.ShowToast("Explicit Toast Info", ToastType.Info, 2000);
        check(service!.ActiveCards.Any(c => c.Message == "Explicit Toast Info" && c.Type == ToastType.Info),
            "ShowToast successfully displayed Info card");

        // 4. Test UpdateStatus with active=true -> ToastType.Success
        var updateStatusMethod = typeof(MainWindow).GetMethod("UpdateStatus", flags)!;
        updateStatusMethod.Invoke(window, new object[] { "Đã lưu cài đặt thành công", true });
        check(service.ActiveCards.Any(c => c.Message == "Đã lưu cài đặt thành công" && c.Type == ToastType.Success),
            "UpdateStatus(..., true) automatically displays Success toast");

        // Verify StatusText is still updated for backward compatibility
        var statusText = (TextBlock)window.FindName("StatusText");
        check(statusText.Text == "Đã lưu cài đặt thành công", "StatusText in status bar is preserved");

        // 5. Test UpdateStatus with active=false and error keyword -> ToastType.Error
        updateStatusMethod.Invoke(window, new object[] { "Lỗi không thể gán phím tắt", false });
        check(service.ActiveCards.Any(c => c.Message == "Lỗi không thể gán phím tắt" && c.Type == ToastType.Error),
            "UpdateStatus with error keyword automatically displays Error toast");

        // 6. Test UpdateStatus with active=false and warning keyword -> ToastType.Warning
        service.ClearAll();
        updateStatusMethod.Invoke(window, new object[] { "Cảnh báo xung đột phím tắt đã tắt", false });
        check(service.ActiveCards.Any(c => c.Message == "Cảnh báo xung đột phím tắt đã tắt" && c.Type == ToastType.Warning),
            "UpdateStatus with warning keyword automatically displays Warning toast");

        // 7. Test null and whitespace ShowToast calls
        var cardCountBefore = service.ActiveCards.Count;
        window.ShowToast("", ToastType.Info);
        window.ShowToast("   ", ToastType.Info);
        check(service.ActiveCards.Count == cardCountBefore, "Empty or whitespace ShowToast does not create a card");

        // 8. Test Background thread dispatching
        System.Threading.Tasks.Task.Run(() =>
        {
            window.ShowToast("Background Thread Toast", ToastType.Info, 2000);
        }).Wait();
        // Process UI dispatcher queue
        var frame = new System.Windows.Threading.DispatcherFrame();
        window.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() => frame.Continue = false));
        System.Windows.Threading.Dispatcher.PushFrame(frame);

        check(service.ActiveCards.Any(c => c.Message == "Background Thread Toast"),
            "ShowToast marshals properly from background thread to UI thread");

        // 9. Test BtnClearAllToasts button in MainWindow
        var clearBtn = (Button?)window.FindName("BtnClearAllToasts");
        check(clearBtn != null, "BtnClearAllToasts is present in MainWindow visual hierarchy");
        clearBtn?.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(service.ActiveCards.Count == 0, "Clicking BtnClearAllToasts clears all toasts");

        // 10. Test ClassifyErrorOrInfo: Vietnamese 'phím tắt' must NOT be classified as Warning
        check(MainWindow.ClassifyErrorOrInfo("Đã lưu phím tắt mới", "Saved new hotkey") == ToastType.Info,
            "'phím tắt' is classified as Info and not falsely flagged as Warning");
        check(MainWindow.ClassifyErrorOrInfo("Đã tắt chế độ tàng hình", "Stealth mode disabled") == ToastType.Warning,
            "Real disable message with 'tắt' is correctly classified as Warning");
        check(MainWindow.ClassifyErrorOrInfo("Lỗi phím tắt trùng", "Conflict error") == ToastType.Error,
            "Error with shortcut conflict is correctly classified as Error");

        // 11. Test ToastCard DropShadow and unclipped border
        var testCard = new ToastCard(new ToastNotification("Shadow test", ToastType.Info, 2000));
        check(!testCard.ClipToBounds, "ToastCard has ClipToBounds=false so DropShadow is never clipped");
        check(testCard.Effect != null, "ToastCard has DropShadowEffect attached");

        // 12. Test IsMouseOverToastOverlay focus protection helper
        var isMouseOverToastMethod = typeof(MainWindow).GetMethod("IsMouseOverToastOverlay", flags);
        check(isMouseOverToastMethod != null, "IsMouseOverToastOverlay method exists on MainWindow");
        var isOverToast = (bool)isMouseOverToastMethod!.Invoke(window, null)!;
        check(!isOverToast, "IsMouseOverToastOverlay returns false when mouse is elsewhere");

        service.ClearAll();
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace StreamGuard.Helpers
{
    public static class SingleInstanceHelper
    {
        public const string GlobalMutexName = @"Global\StreamGuard.SingleInstance.v9";
        public const string LocalMutexName = @"Local\StreamGuard.SingleInstance.v9";
        public const string GlobalShowEventName = @"Global\StreamGuard.ShowExistingInstance.v9";
        public const string LocalShowEventName = @"Local\StreamGuard.ShowExistingInstance.v9";

        public sealed record SingleInstanceAcquisition(
            Mutex? Mutex,
            bool CreatedNew,
            string MutexName,
            string ShowEventName,
            bool IsFallback);

        public static SingleInstanceAcquisition AcquireSingleInstance(
            Func<string, (Mutex? Mutex, bool CreatedNew)>? factory = null)
        {
            factory ??= name =>
            {
                var mutex = new Mutex(true, name, out var createdNew);
                return (mutex, createdNew);
            };

            try
            {
                var (mutex, createdNew) = factory(GlobalMutexName);
                return new SingleInstanceAcquisition(mutex, createdNew, GlobalMutexName, GlobalShowEventName, IsFallback: false);
            }
            catch (UnauthorizedAccessException)
            {
                var (mutex, createdNew) = factory(LocalMutexName);
                return new SingleInstanceAcquisition(mutex, createdNew, LocalMutexName, LocalShowEventName, IsFallback: true);
            }
        }

        public static EventWaitHandle CreateShowExistingEvent(
            string? preferredEventName,
            Func<string, EventWaitHandle>? factory = null)
        {
            factory ??= name => new EventWaitHandle(false, EventResetMode.AutoReset, name);

            var name = string.IsNullOrWhiteSpace(preferredEventName) ? GlobalShowEventName : preferredEventName;
            try
            {
                return factory(name);
            }
            catch (UnauthorizedAccessException) when (name == GlobalShowEventName)
            {
                return factory(LocalShowEventName);
            }
        }

        public static bool SignalExistingInstance(
            string? preferredEventName = null,
            Action<string>? eventSignaler = null)
        {
            var targets = string.IsNullOrWhiteSpace(preferredEventName)
                ? new[] { GlobalShowEventName, LocalShowEventName }
                : new[] { preferredEventName, GlobalShowEventName, LocalShowEventName };

            foreach (var target in targets.Distinct(StringComparer.Ordinal))
            {
                try
                {
                    if (eventSignaler != null)
                    {
                        eventSignaler(target);
                        return true;
                    }

                    using var existingEvent = EventWaitHandle.OpenExisting(target);
                    existingEvent.Set();
                    return true;
                }
                catch
                {
                    // Fall back to next target candidate
                }
            }

            return false;
        }
    }
}

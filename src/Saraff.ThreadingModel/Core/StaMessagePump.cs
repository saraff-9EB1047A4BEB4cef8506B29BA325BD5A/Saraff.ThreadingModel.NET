using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace Saraff.ThreadingModel.Core {

    internal sealed class StaMessagePump {
        private const uint QS_ALLINPUT = 0x04FF;
        private const uint PM_REMOVE = 0x0001;
        private const int WAIT_TIMEOUT = 258;
        private const int WAIT_FAILED = -1;

        internal const int MessageReady = -2;
        internal const int TimedOut = -3;

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeMessage {
            public IntPtr hWnd;
            public uint message;
            public IntPtr wParam;
            public IntPtr lParam;
            public uint time;
            public int x;
            public int y;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int MsgWaitForMultipleObjects(int count, IntPtr[] handles, bool waitAll, int milliseconds, uint wakeMask);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool PeekMessage(out NativeMessage message, IntPtr hWnd, uint filterMin, uint filterMax, uint remove);

        [DllImport("user32.dll")]
        private static extern bool TranslateMessage(ref NativeMessage message);

        [DllImport("user32.dll")]
        private static extern IntPtr DispatchMessage(ref NativeMessage message);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        internal int ThreadId { get; }

        public StaMessagePump() {
            NativeMessage message;
            PeekMessage(out message, IntPtr.Zero, 0, 0, 0);
            this.ThreadId = unchecked((int)GetCurrentThreadId());
        }

        internal int Wait(WaitHandle[] handles, TimeSpan timeout) {
            var native = new IntPtr[handles.Length];
            for(var i = 0; i < handles.Length; i++) {
                native[i] = handles[i].SafeWaitHandle.DangerousGetHandle();
            }
            var milliseconds = timeout == Timeout.InfiniteTimeSpan
                ? -1
                : (int)Math.Min(int.MaxValue, Math.Max(0, Math.Ceiling(timeout.TotalMilliseconds)));
            var result = MsgWaitForMultipleObjects(native.Length, native, false, milliseconds, QS_ALLINPUT);
            if(result == WAIT_FAILED) {
                throw new InvalidOperationException("MsgWaitForMultipleObjects завершился с ошибкой " + Marshal.GetLastWin32Error() + ".");
            }
            if(result == WAIT_TIMEOUT) {
                return TimedOut;
            }
            if(result >= 0 && result < native.Length) {
                this.PumpPending();
                return result;
            }
            this.PumpPending();
            return MessageReady;
        }

        internal void PumpPending() {
            NativeMessage message;
            while(PeekMessage(out message, IntPtr.Zero, 0, 0, PM_REMOVE)) {
                if(message.message == 0) {
                    continue;
                }
                TranslateMessage(ref message);
                DispatchMessage(ref message);
            }
        }
    }
}

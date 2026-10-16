using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;

namespace StickRpg4k {
    // Ruffle reserves Escape for exiting fullscreen. Remap only physical Escape
    // directed at this session's foreground window to the game's F13 menu key.
    public sealed class EscMenuBridge : IDisposable {
        private readonly uint processId;
        private readonly Thread thread;
        private readonly ManualResetEvent ready = new ManualResetEvent(false);
        private HookCallback callback;
        private IntPtr hook;
        private uint threadId;
        private Exception failure;
        private bool held;
        private delegate IntPtr HookCallback(int code, IntPtr message, IntPtr data);
        public EscMenuBridge(int childProcessId) {
            processId=(uint)childProcessId;
            thread=new Thread(Run);thread.IsBackground=true;thread.Name="StickRPG4K menu key";
            thread.SetApartmentState(ApartmentState.STA);thread.Start();ready.WaitOne();
            if(failure!=null){ready.Dispose();throw new InvalidOperationException("Could not register the game menu key.",failure);}
        }
        private void Run() {
            try {
                threadId=GetCurrentThreadId();
                callback=OnKey;
                hook=SetWindowsHookEx(13,callback,GetModuleHandle(null),0);
                if(hook==IntPtr.Zero)throw new Win32Exception(Marshal.GetLastWin32Error());
                // Create the thread's message queue before advertising readiness.
                Message message;PeekMessage(out message,IntPtr.Zero,0,0,0);
                ready.Set();
                while(GetMessage(out message,IntPtr.Zero,0,0)>0){TranslateMessage(ref message);DispatchMessage(ref message);}
            } catch(Exception error){failure=error;ready.Set();}
            finally {if(hook!=IntPtr.Zero){UnhookWindowsHookEx(hook);hook=IntPtr.Zero;}}
        }
        private IntPtr OnKey(int code,IntPtr message,IntPtr data) {
            if(code>=0 && Marshal.ReadInt32(data)==27) {
                IntPtr window=GetForegroundWindow();uint owner;GetWindowThreadProcessId(window,out owner);
                if(RoutesToMenu(code,Marshal.ReadInt32(data),owner,processId,message.ToInt32())) {
                    int kind=message.ToInt32();
                    if(kind==0x100 || kind==0x104) {
                        if(!held){held=true;PostMessage(window,0x100,new IntPtr(124),KeyMessage(false));}
                        return new IntPtr(1);
                    }
                    if(kind==0x101 || kind==0x105) {
                        held=false;PostMessage(window,0x101,new IntPtr(124),KeyMessage(true));return new IntPtr(1);
                    }
                } else held=false;
            }
            return CallNextHookEx(hook,code,message,data);
        }
        public static bool RoutesToMenu(int code,int key,uint owner,uint game,int message) {
            return code>=0 && key==27 && owner==game &&
                   (message==0x100 || message==0x101 || message==0x104 || message==0x105);
        }
        public static IntPtr KeyMessage(bool released) {
            uint value=1u | (MapVirtualKey(124,0)<<16);
            if(released)value|=0xc0000000u;
            return new IntPtr(unchecked((int)value));
        }
        public void Dispose() {
            if(thread.IsAlive){PostThreadMessage(threadId,0x12,IntPtr.Zero,IntPtr.Zero);thread.Join();}
            ready.Dispose();GC.KeepAlive(callback);
        }
        [StructLayout(LayoutKind.Sequential)] private struct Message {public IntPtr Window;public uint Id;public IntPtr WParam,LParam;public uint Time;public int X,Y;public uint Private;}
        [DllImport("user32.dll",SetLastError=true)] private static extern IntPtr SetWindowsHookEx(int id,HookCallback callback,IntPtr module,uint thread);
        [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook,int code,IntPtr message,IntPtr data);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window,out uint process);
        [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr window,uint message,IntPtr wparam,IntPtr lparam);
        [DllImport("user32.dll")] private static extern bool PostThreadMessage(uint thread,uint message,IntPtr wparam,IntPtr lparam);
        [DllImport("user32.dll")] private static extern int GetMessage(out Message message,IntPtr window,uint minimum,uint maximum);
        [DllImport("user32.dll")] private static extern bool PeekMessage(out Message message,IntPtr window,uint minimum,uint maximum,uint remove);
        [DllImport("user32.dll")] private static extern bool TranslateMessage(ref Message message);
        [DllImport("user32.dll")] private static extern IntPtr DispatchMessage(ref Message message);
        [DllImport("user32.dll")] private static extern uint MapVirtualKey(uint key,uint type);
        [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
        [DllImport("kernel32.dll",CharSet=CharSet.Auto)] private static extern IntPtr GetModuleHandle(string name);
    }
}

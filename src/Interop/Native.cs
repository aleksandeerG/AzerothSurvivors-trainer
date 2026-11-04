using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace AzerothTrainer
{
    static class Native
    {
        [DllImport("kernel32.dll", SetLastError=true)] internal static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
        [DllImport("kernel32.dll", SetLastError=true)] internal static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll", SetLastError=true)] internal static extern bool ReadProcessMemory(IntPtr h, IntPtr address, byte[] bytes, UIntPtr length, out UIntPtr read);
        [DllImport("kernel32.dll", SetLastError=true)] internal static extern bool WriteProcessMemory(IntPtr h, IntPtr address, byte[] bytes, UIntPtr length, out UIntPtr written);
        [DllImport("kernel32.dll", SetLastError=true)] internal static extern IntPtr VirtualAllocEx(IntPtr h, IntPtr address, UIntPtr size, uint type, uint protect);
        [DllImport("kernel32.dll", SetLastError=true)] internal static extern bool VirtualFreeEx(IntPtr h, IntPtr address, UIntPtr size, uint type);
        [DllImport("kernel32.dll", SetLastError=true)] internal static extern bool VirtualProtectEx(IntPtr h, IntPtr address, UIntPtr size, uint protect, out uint old);
        [DllImport("kernel32.dll", SetLastError=true)] internal static extern bool FlushInstructionCache(IntPtr h, IntPtr address, UIntPtr size);
        [DllImport("kernel32.dll", SetLastError=true)] internal static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint pid);
        [DllImport("kernel32.dll", SetLastError=true)] internal static extern bool Thread32First(IntPtr snapshot, ref ThreadEntry entry);
        [DllImport("kernel32.dll", SetLastError=true)] internal static extern bool Thread32Next(IntPtr snapshot, ref ThreadEntry entry);
        [DllImport("kernel32.dll", SetLastError=true)] internal static extern IntPtr OpenThread(uint access, bool inherit, uint tid);
        [DllImport("kernel32.dll", SetLastError=true)] internal static extern uint SuspendThread(IntPtr thread);
        [DllImport("kernel32.dll", SetLastError=true)] internal static extern uint ResumeThread(IntPtr thread);
        [DllImport("kernel32.dll", SetLastError=true)] internal static extern bool GetThreadContext(IntPtr thread, IntPtr context);
        [DllImport("kernel32.dll", SetLastError=true)] internal static extern IntPtr CreateRemoteThread(IntPtr process,IntPtr attributes,UIntPtr stack,IntPtr entry,IntPtr parameter,uint flags,IntPtr threadId);
        [DllImport("kernel32.dll", SetLastError=true)] internal static extern uint WaitForSingleObject(IntPtr handle,uint milliseconds);
        [DllImport("kernel32.dll", CharSet=CharSet.Unicode)] internal static extern IntPtr GetModuleHandle(string name);
        [DllImport("kernel32.dll", CharSet=CharSet.Ansi)] internal static extern IntPtr GetProcAddress(IntPtr module,string name);
        [StructLayout(LayoutKind.Sequential)] internal struct ThreadEntry
        {
            internal uint size, usage, tid, pid;
            internal int priority, delta;
            internal uint flags;
        }
        internal static Exception Error(string action) { return new Win32Exception(Marshal.GetLastWin32Error(), action); }
        internal static IntPtr Add(IntPtr p, long n) { return new IntPtr(p.ToInt64()+n); }
        internal static byte[] Read(IntPtr h, IntPtr p, int count)
        {
            byte[] bytes = new byte[count]; UIntPtr done;
            if (!ReadProcessMemory(h,p,bytes,(UIntPtr)count,out done) || done.ToUInt64()!=(ulong)count) throw Error("Cannot read game memory");
            return bytes;
        }
        internal static void Write(IntPtr h, IntPtr p, byte[] bytes)
        {
            UIntPtr done;
            if (!WriteProcessMemory(h,p,bytes,(UIntPtr)bytes.Length,out done) || done.ToUInt64()!=(ulong)bytes.Length) throw Error("Cannot write game memory");
        }
        internal static bool Equal(byte[] a, byte[] b)
        {
            if (a.Length!=b.Length) return false;
            for(int i=0;i<a.Length;i++) if(a[i]!=b[i]) return false;
            return true;
        }
    }

    // Suspension prevents a thread from fetching a partly written jump. Check RIP too.
    sealed class FrozenThreads : IDisposable
    {
        readonly List<IntPtr> threads=new List<IntPtr>();
        internal readonly List<long> InstructionPointers=new List<long>();
        internal FrozenThreads(int pid)
        {
            var seen=new HashSet<uint>();
            IntPtr buffer=Marshal.AllocHGlobal(1264);
            IntPtr context=new IntPtr((buffer.ToInt64()+15)&~15L);
            try
            {
                // A newly created thread may miss the first snapshot. Keep all
                // captured threads suspended until a full pass finds no new IDs.
                for(int pass=0;pass<16;pass++)
                {
                    bool added=false;IntPtr snapshot=Native.CreateToolhelp32Snapshot(4,0);
                    if(snapshot==new IntPtr(-1))throw Native.Error("Cannot list game threads");
                    try
                    {
                        Native.ThreadEntry e=new Native.ThreadEntry();e.size=(uint)Marshal.SizeOf(e);
                        if(!Native.Thread32First(snapshot,ref e))throw Native.Error("Cannot read game threads");
                        do
                        {
                            if(e.pid!=(uint)pid||seen.Contains(e.tid))continue;
                            IntPtr t=Native.OpenThread(0x4A,false,e.tid);
                            if(t==IntPtr.Zero)
                            {
                                if(Marshal.GetLastWin32Error()==87)continue; // Thread already exited.
                                throw Native.Error("Cannot open a game thread");
                            }
                            if(Native.SuspendThread(t)==UInt32.MaxValue)
                            {
                                int error=Marshal.GetLastWin32Error();Native.CloseHandle(t);
                                if(error==6||error==87)continue;
                                throw new Win32Exception(error,"Cannot pause game thread");
                            }
                            threads.Add(t);seen.Add(e.tid);added=true;
                            Marshal.WriteInt32(context,48,0x100001); // AMD64 CONTEXT_CONTROL
                            if(!Native.GetThreadContext(t,context))throw Native.Error("Cannot inspect paused game thread");
                            InstructionPointers.Add(Marshal.ReadInt64(context,248));
                        }while(Native.Thread32Next(snapshot,ref e));
                        if(Marshal.GetLastWin32Error()!=18)throw Native.Error("Cannot finish reading game threads");
                    }
                    finally{Native.CloseHandle(snapshot);}
                    if(threads.Count==0)throw new Exception("The game has exited.");
                    if(!added)return;
                }
                throw new Exception("Game threads did not settle. Retry in a moment.");
            }
            catch { Dispose(); throw; }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        internal void RequireOutside(IntPtr address, int count)
        {
            long p=address.ToInt64();
            foreach(long ip in InstructionPointers)
                if(ip>=p && ip<p+count) throw new Exception("The game is executing code this change needs to replace. Try again in a moment.");
        }
        public void Dispose()
        {
            for(int i=threads.Count-1;i>=0;i--){ Native.ResumeThread(threads[i]); Native.CloseHandle(threads[i]); }
            threads.Clear();
        }
    }

}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Session;

// Capture only scheduling intervals of the preview PID. No stacks or application payloads.
internal static class PreviewCpuTrace {
    static int Main(string[] args){
        int pid=int.Parse(args[0]);string dir=args[1];
        try{
            using(var proc=Process.GetProcessById(pid))
            using(var session=new TraceEventSession("hahhub-preview-cpu-"+pid))
            using(var log=new StreamWriter(Path.Combine(dir,"cpu-slices.csv"))){
                var threads=new HashSet<int>();
                var active=new Dictionary<int,Tuple<int,long>>();
                session.StopOnDispose=true;
                session.EnableKernelProvider(KernelTraceEventParser.Keywords.Thread|KernelTraceEventParser.Keywords.ContextSwitch);
                var source=session.Source;
                source.Kernel.ThreadStart+=e=>{if(e.ProcessID==pid)threads.Add(e.ThreadID);else threads.Remove(e.ThreadID);};
                source.Kernel.ThreadStop+=e=>threads.Remove(e.ThreadID);
                source.Kernel.ThreadCSwitch+=e=>{
                    int core=e.ProcessorNumber;
                    Tuple<int,long> previous;
                    bool outgoing=active.TryGetValue(core,out previous);
                    bool incoming=threads.Contains(e.NewThreadID);
                    if(!outgoing&&!incoming)return;
                    var at=e.TimeStamp.ToFileTimeUtc();
                    if(outgoing){
                        if(previous.Item1==e.OldThreadID)log.WriteLine(previous.Item2+","+at);
                        active.Remove(core);
                    }
                    if(incoming)active[core]=Tuple.Create(e.NewThreadID,at);
                };
                foreach(ProcessThread thread in proc.Threads)threads.Add(thread.Id);
                long begin=DateTime.UtcNow.ToFileTimeUtc();
                File.WriteAllText(Path.Combine(dir,"cpu-start.txt"),begin.ToString());
                var stop=new Thread(()=>{proc.WaitForExit();Thread.Sleep(1200);session.Stop();}){IsBackground=true};
                stop.Start();source.Process();
                log.Flush();File.WriteAllText(Path.Combine(dir,"cpu-end.txt"),DateTime.UtcNow.ToFileTimeUtc()+","+source.EventsLost);
            }
            return 0;
        }catch(Exception e){File.WriteAllText(Path.Combine(dir,"cpu-error.txt"),e.ToString());return 1;}
    }
}

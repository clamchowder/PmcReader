using System.Collections.Generic;

namespace PmcReader.AMD
{
    public class Zen5 : Amd19hCpu
    {
        public Zen5()
        {
            List<MonitoringConfig> configList = new List<MonitoringConfig>
            {
                new Zen4TopDown(this, 8),
                new Zen4TDFrontend(this, 8),
                new Zen4TDBackend(this, 8),
                new FetchConfig(this),
                new FrontendOps(this),
                new DispatchStall(this),
                new DispatchStallSched(this),
                new L2Config(this),
                new FpPipes(this),
            };
            monitoringConfigs = configList.ToArray();
            architectureName = "Zen 5";
        }

        public class FrontendOps : MonitoringConfig
        {
            private Zen5 cpu;
            public string GetConfigName() { return "Ops from Frontend"; }

            public FrontendOps(Zen5 amdCpu) { cpu = amdCpu; }

            public string[] GetColumns() { return columns; }

            public void Initialize()
            {
                cpu.ProgramPerfCounters(
                    GetPerfCtlValue(0xAA, 1, true, true, false, false, true, false, cmask: 1, 0, false, false),  // decoder active
                    GetPerfCtlValue(0xAA, 0b10, true, true, false, false, true, false, cmask: 1, 0, false, false),  // oc active
                    GetPerfCtlValue(0xAA, 1, true, true, false, false, true, false, 0, 0, false, false),  // uop from decoder
                    GetPerfCtlValue(0xAA, 0b10, true, true, false, false, true, false, 0, 0, false, false), // uop from op cache
                    GetPerfCtlValue(0xAB, 8, true, true, false, false, true, false, 0, 0, false, false), // integer dispatch
                    GetPerfCtlValue(0xAB, 4, true, true, false, false, true, false, 0, 0, false, false)  // fp dispatch
                    ); 
            }

            public MonitoringUpdateResults Update()
            {
                MonitoringUpdateResults results = new MonitoringUpdateResults();
                results.unitMetrics = new string[cpu.GetThreadCount()][];
                cpu.InitializeCoreTotals();
                for (int threadIdx = 0; threadIdx < cpu.GetThreadCount(); threadIdx++)
                {
                    cpu.UpdateThreadCoreCounterData(threadIdx);
                    results.unitMetrics[threadIdx] = computeMetrics("Thread " + threadIdx, cpu.NormalizedThreadCounts[threadIdx]);
                }

                results.overallMetrics = computeMetrics("Overall", cpu.NormalizedTotalCounts);
                results.overallCounterValues = cpu.GetOverallCounterValues("Decoder Cycles", "OC Cycles", "Decoder Ops", "OC Ops", "Integer op dispatch", "FP op dispatch");
                return results;
            }

            public string[] columns = new string[] { "Item", "Active Cycles", "Instructions", "IPC", "OC %", "Decoder %", "OC Active", "Decoder Active", "OC Ops", "Decoder Ops", "Integer Ops", "FP Ops" };

            public string GetHelpText()
            {
                return "";
            }

            private string[] computeMetrics(string label, NormalizedCoreCounterData counterData)
            {
                float totalOps = counterData.ctr2 + counterData.ctr3;
                float totalDispatchedOps = counterData.ctr4 + counterData.ctr5;
                return new string[] { label,
                        FormatLargeNumber(counterData.aperf),
                        FormatLargeNumber(counterData.instr),
                        string.Format("{0:F2}", counterData.instr / counterData.aperf),
                        FormatPercentage(counterData.ctr3, totalOps),
                        FormatPercentage(counterData.ctr2, totalOps),
                        FormatPercentage(counterData.ctr1, counterData.aperf),
                        FormatPercentage(counterData.ctr0, counterData.aperf),
                        FormatLargeNumber(counterData.ctr3),
                        FormatLargeNumber(counterData.ctr2),
                        FormatLargeNumber(counterData.ctr4),
                        FormatLargeNumber(counterData.ctr5)
                        };
            }
        }

        public class DispatchStall : MonitoringConfig
        {
            private Zen5 cpu;
            public string GetConfigName() { return "Dispatch Stalls"; }

            public DispatchStall(Zen5 amdCpu) { cpu = amdCpu; }

            public string[] GetColumns() { return columns; }

            public void Initialize()
            {
                cpu.ProgramPerfCounters(GetPerfCtlValue(0xAF, (byte)(1U << 5), true, true, false, false, true, false, 0, 0, false, false), // ROB
                    GetPerfCtlValue(0xAE, 1, true, true, false, false, true, false, 0, 0, false, false), // integer RF
                    GetPerfCtlValue(0xAE, (byte)(1U << 6), true, true, false, false, true, false, 0, 0, false, false), // FP NSQ
                    GetPerfCtlValue(0xAE, 2, true, true, false, false, true, false, 0, 0, false, false), // LDQ
                    GetPerfCtlValue(0xAE, 4, true, true, false, false, true, false, 0, 0, false, false), // STQ
                    GetPerfCtlValue(0xAE, 0x10, true, true, false, false, true, false, 0, 0, false, false)); // taken branch buffer
            }

            public MonitoringUpdateResults Update()
            {
                MonitoringUpdateResults results = new MonitoringUpdateResults();
                results.unitMetrics = new string[cpu.GetThreadCount()][];
                cpu.InitializeCoreTotals();
                for (int threadIdx = 0; threadIdx < cpu.GetThreadCount(); threadIdx++)
                {
                    cpu.UpdateThreadCoreCounterData(threadIdx);
                    results.unitMetrics[threadIdx] = computeMetrics("Thread " + threadIdx, cpu.NormalizedThreadCounts[threadIdx]);
                }

                results.overallMetrics = computeMetrics("Overall", cpu.NormalizedTotalCounts);
                results.overallCounterValues = cpu.GetOverallCounterValues("ROB", "INT Regs", "FP NSQ", "LDQ", "STQ", "Taken Branch Buffer");
                return results;
            }

            public string[] columns = new string[] { "Item", "Active Cycles", "Instructions", "IPC", "ROB", "INT Regs", "FP NSQ", "LDQ", "STQ", "Taken Branch Buffer"};

            public string GetHelpText()
            {
                return "";
            }

            private string[] computeMetrics(string label, NormalizedCoreCounterData counterData)
            {
                return new string[] { label,
                        FormatLargeNumber(counterData.aperf),
                        FormatLargeNumber(counterData.instr),
                        string.Format("{0:F2}", counterData.instr / counterData.aperf),
                        FormatPercentage(counterData.ctr0, counterData.aperf),
                        FormatPercentage(counterData.ctr2, counterData.aperf),
                        FormatPercentage(counterData.ctr4, counterData.aperf),
                        FormatPercentage(counterData.ctr1, counterData.aperf),
                        FormatPercentage(counterData.ctr3, counterData.aperf),
                        FormatPercentage(counterData.ctr5, counterData.aperf)
                        };
            }
        }

        public class DispatchStallSched : MonitoringConfig
        {
            private Zen5 cpu;
            public string GetConfigName() { return "Dispatch Stalls (Sched)"; }

            public DispatchStallSched(Zen5 amdCpu) { cpu = amdCpu; }

            public string[] GetColumns() { return columns; }

            public void Initialize()
            {
                cpu.ProgramPerfCounters(GetPerfCtlValue(0xAF, 1, true, true, false, false, true, false, 0, 0, false, false), // ALU Tokens
                    GetPerfCtlValue(0xAF, 2, true, true, false, false, true, false, 0, 0, false, false), // AGU Tokens
                    GetPerfCtlValue(0xAE, (byte)(1U << 6), true, true, false, false, true, false, 0, 0, false, false), // FP NSQ
                    GetPerfCtlValue(0xAE, 4, true, true, false, false, true, false, 0, 0, false, false), // integer execution flush
                    GetPerfCtlValue(0xA2, 0x30, true, true, false, false, true, false, 0, 1, false, false), // misc
                    GetPerfCtlValue(0xAE, 0x10, true, true, false, false, true, false, 0, 0, false, false)); // taken branch buffer (unused)
            }

            public MonitoringUpdateResults Update()
            {
                MonitoringUpdateResults results = new MonitoringUpdateResults();
                results.unitMetrics = new string[cpu.GetThreadCount()][];
                cpu.InitializeCoreTotals();
                for (int threadIdx = 0; threadIdx < cpu.GetThreadCount(); threadIdx++)
                {
                    cpu.UpdateThreadCoreCounterData(threadIdx);
                    results.unitMetrics[threadIdx] = computeMetrics("Thread " + threadIdx, cpu.NormalizedThreadCounts[threadIdx]);
                }

                results.overallMetrics = computeMetrics("Overall", cpu.NormalizedTotalCounts);
                results.overallCounterValues = cpu.GetOverallCounterValues("ALU Scheduler", "AGU Scheduler", "FP NSQ", "Integer Execution Flush", "Misc", "Taken Branch Buffer");
                return results;
            }

            public string[] columns = new string[] { "Item", "Active Cycles", "Instructions", "IPC", "ALU Scheduler", "AGU Scheduler", "FP NSQ", "Integer Execution Flush", "Misc", "Taken Branch Buffer" };

            public string GetHelpText()
            {
                return "";
            }

            private string[] computeMetrics(string label, NormalizedCoreCounterData counterData)
            {
                return new string[] { label,
                        FormatLargeNumber(counterData.aperf),
                        FormatLargeNumber(counterData.instr),
                        string.Format("{0:F2}", counterData.instr / counterData.aperf),
                        FormatPercentage(counterData.ctr0, counterData.aperf),
                        FormatPercentage(counterData.ctr2, counterData.aperf),
                        FormatPercentage(counterData.ctr4, counterData.aperf),
                        FormatPercentage(counterData.ctr1, counterData.aperf),
                        FormatPercentage(counterData.ctr3, counterData.aperf),
                        FormatPercentage(counterData.ctr5, counterData.aperf)
                        };
            }
        }

        public class FpPipes : MonitoringConfig
        {
            private Zen5 cpu;
            public string GetConfigName() { return "FP Pipes (undoc)"; }

            public FpPipes(Zen5 amdCpu) { cpu = amdCpu; }

            public string[] GetColumns() { return columns; }

            public void Initialize()
            {
                cpu.ProgramPerfCounters(GetPerfCtlValue(0, 1, true, true, false, false, true, false, 0, 0, false, false),
                    GetPerfCtlValue(0, 2, true, true, false, false, true, false, 0, 0, false, false), 
                    GetPerfCtlValue(0, 4, true, true, false, false, true, false, 0, 0, false, false),
                    GetPerfCtlValue(0, 8, true, true, false, false, true, false, 0, 0, false, false),
                    GetPerfCtlValue(0, 0x10, true, true, false, false, true, false, 0, 0, false, false),
                    GetPerfCtlValue(0, 0x20, true, true, false, false, true, false, 0, 0, false, false));
            }

            public MonitoringUpdateResults Update()
            {
                MonitoringUpdateResults results = new MonitoringUpdateResults();
                results.unitMetrics = new string[cpu.GetThreadCount()][];
                cpu.InitializeCoreTotals();
                for (int threadIdx = 0; threadIdx < cpu.GetThreadCount(); threadIdx++)
                {
                    cpu.UpdateThreadCoreCounterData(threadIdx);
                    results.unitMetrics[threadIdx] = computeMetrics("Thread " + threadIdx, cpu.NormalizedThreadCounts[threadIdx]);
                }

                results.overallMetrics = computeMetrics("Overall", cpu.NormalizedTotalCounts);
                results.overallCounterValues = cpu.GetOverallCounterValues("0", "1", "2", "3", "4", "5");
                return results;
            }

            public string[] columns = new string[] { "Item", "Active Cycles", "Instructions", "IPC", "P0 FMA", "P2 FADD", "P4 FStore", "P1 FMA", "P3 FADD", "P5 FStore" };

            public string GetHelpText()
            {
                return "";
            }

            private string[] computeMetrics(string label, NormalizedCoreCounterData counterData)
            {
                return new string[] { label,
                        FormatLargeNumber(counterData.aperf),
                        FormatLargeNumber(counterData.instr),
                        string.Format("{0:F2}", counterData.instr / counterData.aperf),
                        FormatPercentage(counterData.ctr0, counterData.aperf),
                        FormatPercentage(counterData.ctr2, counterData.aperf),
                        FormatPercentage(counterData.ctr4, counterData.aperf),
                        FormatPercentage(counterData.ctr1, counterData.aperf),
                        FormatPercentage(counterData.ctr3, counterData.aperf),
                        FormatPercentage(counterData.ctr5, counterData.aperf)
                        };
            }
        }
    }
}

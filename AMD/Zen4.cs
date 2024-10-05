﻿using System;
using System.Collections.Generic;
using PmcReader.Interop;

namespace PmcReader.AMD
{
    public class Zen4 : Amd19hCpu
    {
        public Zen4()
        {
            List<MonitoringConfig> configList = new List<MonitoringConfig>
            {
                new Zen4TopDown(this),
                new Zen4TDFrontend(this),
                new Zen4TDBackend(this),
                new BpuMonitoringConfig(this),
                new FetchConfig(this),
                new FrontendOpsConfig(this),
                new DispatchStallConfig(this),
                new DispatchStallConfig1(this),
                new DCFillConfig(this),
                new DemandDCFillConfig(this),
                new SwPrefetchConfig(this),
                new FlopsConfig(this),
                new VecIntConfig(this),
                new FpOpWidthConfig(this),
                new FpPipes(this),
                new LocksConfig(this),
                new L2Config(this),
                new L1DBw(this),
                new MABOccupancyConfig(this),
                new TlbConfig(this)
            };
            monitoringConfigs = configList.ToArray();
            architectureName = "Zen 4";
        }

        public class Zen4TopDown : MonitoringConfig
        {
            private Zen4 cpu;
            public string GetConfigName() { return "Top Down, Dispatch"; }

            public Zen4TopDown(Zen4 amdCpu) { cpu = amdCpu; }

            public string[] GetColumns() { return columns; }

            public void Initialize()
            {
                ulong frontendBoundSlots = 0x1004301A0;
                ulong decoderOps = 0x4307AA;
                ulong retiredOps = 0x4300C1;
                ulong backendBoundSlots = 0x100431EA0;
                ulong smtContentionSlots = 0x1004360A0;
                ulong microcodeOps = 0x1004300C2;
                cpu.ProgramPerfCounters(frontendBoundSlots, decoderOps, retiredOps, backendBoundSlots, smtContentionSlots, microcodeOps);
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
                results.overallCounterValues = cpu.GetOverallCounterValues("Frontend Bound Slots", "Decoder Ops", "Retired Ops", "Backend Bound Slots", "SMT Contention Slots", "Retired Microcode Ops");
                return results;
            }

            public string[] columns = new string[] { "Item", "Active Cycles", "Instructions", "IPC",
                "Bad Speculation", "Frontend Bound", "Backend Bound", "SMT Contention", "Microcoded Ops" };

            public string GetHelpText()
            {
                return "";
            }

            private string[] computeMetrics(string label, NormalizedCoreCounterData counterData)
            {
                float slots = counterData.aperf * 6;
                float frontendBoundSlots = counterData.ctr0;
                float decoderOps = counterData.ctr1;
                float retiredOps = counterData.ctr2;
                float backendBoundSlots = counterData.ctr3;
                float smtContentionSlots = counterData.ctr4;
                float microcodeOps = counterData.ctr5;
                return new string[] { label,
                        FormatLargeNumber(counterData.aperf),
                        FormatLargeNumber(counterData.instr),
                        string.Format("{0:F2}", counterData.instr / counterData.aperf),
                        FormatPercentage(decoderOps - retiredOps, slots),
                        FormatPercentage(frontendBoundSlots, slots),
                        FormatPercentage(backendBoundSlots, slots),
                        FormatPercentage(smtContentionSlots, slots),
                        FormatPercentage(microcodeOps, retiredOps)};
            }
        }

        public class Zen4TDFrontend : MonitoringConfig
        {
            private Zen4 cpu;
            public string GetConfigName() { return "Top Down, Frontend"; }

            public Zen4TDFrontend(Zen4 amdCpu) { cpu = amdCpu; }

            public string[] GetColumns() { return columns; }

            public void Initialize()
            {
                ulong frontendBoundSlots = 0x1004301A0;
                ulong frontendBoundSlotsCmask6 = 0x1064301A0;
                ulong mispredictedBranches = 0x4300C3;
                ulong resyncs = 0x430096;
                ulong decoderOps = 0x4307AA;
                ulong retiredOps = 0x4300C1;
                cpu.ProgramPerfCounters(frontendBoundSlots, decoderOps, retiredOps, frontendBoundSlotsCmask6, mispredictedBranches, resyncs);
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
                results.overallCounterValues = cpu.GetOverallCounterValues("Frontend Bound Slots", "Decoder Ops", "Retired Ops", "Frontend Bound Slots Cmask 6", "Mispredicted Branches", "Resyncs");
                return results;
            }

            public string[] columns = new string[] { "Item", "Active Cycles", "Instructions", "IPC",
                "Frontend Bound", "Frontend Latency", "Frontend BW", "Bad Speculation", "Bad Spec: Mispredicts", "Bad Spec: Resyncs" };

            public string GetHelpText()
            {
                return "";
            }

            private string[] computeMetrics(string label, NormalizedCoreCounterData counterData)
            {
                float slots = counterData.aperf * 6;
                float frontendBoundSlots = counterData.ctr0;
                float decoderOps = counterData.ctr1;
                float retiredOps = counterData.ctr2;
                float frontendBoundCmask6 = counterData.ctr3;
                float mispredictedBranches = counterData.ctr4;
                float resyncs = counterData.ctr5;
                float badSpec = (decoderOps - retiredOps) / slots;
                return new string[] { label,
                        FormatLargeNumber(counterData.aperf),
                        FormatLargeNumber(counterData.instr),
                        string.Format("{0:F2}", counterData.instr / counterData.aperf),
                        FormatPercentage(frontendBoundSlots, slots),
                        FormatPercentage(6 * frontendBoundCmask6, slots),
                        FormatPercentage(frontendBoundSlots - 6 * frontendBoundCmask6, slots),
                        FormatPercentage(decoderOps - retiredOps, slots),
                        FormatPercentage(badSpec * mispredictedBranches, mispredictedBranches + resyncs),
                        FormatPercentage(badSpec * resyncs, mispredictedBranches + resyncs)};
            }
        }

        public class Zen4TDBackend : MonitoringConfig
        {
            private Zen4 cpu;
            public string GetConfigName() { return "Top Down, Backend Retire"; }

            public Zen4TDBackend(Zen4 amdCpu) { cpu = amdCpu; }

            public string[] GetColumns() { return columns; }

            public void Initialize()
            {
                ulong retireCycles = GetPerfCtlValue(0xC1, 1, true, true, false, false, true, false, cmask: 1, 0, false, false);
                ulong backendBoundSlots = 0x100431EA0;
                ulong noRetireBlockedLoad = 0x43A2D6;
                ulong noRetireAnyBlocked = 0x4302D6;
                ulong noRetireEmpty = GetPerfCtlValue(0xD6, 1, true, true, false, false, true, false, 0, 0, false, false);
                ulong noRetireNotSelected = GetPerfCtlValue(0xD6, 2, true, true, false, false, true, false, 0, 0, false, false);
                cpu.ProgramPerfCounters(retireCycles, backendBoundSlots, noRetireBlockedLoad, noRetireAnyBlocked, noRetireEmpty, noRetireNotSelected);
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
                results.overallCounterValues = cpu.GetOverallCounterValues("Retired Ops Cmask 1", "Backend Bound Slots", "No retire: blocked load", "No retire: Not complete", "No retire: ROB empty", "No retire: Not selected");
                return results;
            }

            public string[] columns = new string[] { "Item", "Active Cycles", "Instructions", "IPC",
                "Backend Bound", "Memory Bound", "Core Bound", "Retire SMT Contention", "ROB Empty", "Retire Active" };

            public string GetHelpText()
            {
                return "";
            }

            private string[] computeMetrics(string label, NormalizedCoreCounterData counterData)
            {
                float slots = counterData.aperf * 6;
                float retiredOpsCmask1 = counterData.ctr0;
                float backendBoundSlots = counterData.ctr1;
                float backendBoundPercent = backendBoundSlots / slots;
                float blockedLoad = counterData.ctr2;
                float anyBlocked = counterData.ctr3;
                float robEmpty = counterData.ctr4;
                float notSelected = counterData.ctr5;
                return new string[] { label,
                        FormatLargeNumber(counterData.aperf),
                        FormatLargeNumber(counterData.instr),
                        string.Format("{0:F2}", counterData.instr / counterData.aperf),
                        string.Format("{0:F2}%", backendBoundPercent * 100),
                        string.Format("{0:F2}%", 100 * backendBoundPercent * (blockedLoad / anyBlocked)),  // backend bound, memory
                        string.Format("{0:F2}%", 100 * backendBoundPercent * (1 - (blockedLoad / anyBlocked))), // backend bound, core
                        FormatPercentage(notSelected, counterData.aperf),
                        FormatPercentage(robEmpty, counterData.aperf),
                        FormatPercentage(retiredOpsCmask1, counterData.aperf)
                };
            }
        }

        public class BpuMonitoringConfig : MonitoringConfig
        {
            private Zen4 cpu;
            public string GetConfigName() { return "Branch Prediction"; }

            public BpuMonitoringConfig(Zen4 amdCpu) { cpu = amdCpu; }

            public string[] GetColumns() { return columns; }

            public void Initialize()
            {
                cpu.ProgramPerfCounters(GetPerfCtlValue(0xC2, 0, true, true, false, false, true, false, 0, 0, false, false), // retired branches
                    GetPerfCtlValue(0xC3, 0, true, true, false, false, true, false, 0, 0, false, false),  // mispredicted retired branches
                    GetPerfCtlValue(0x8B, 0, true, true, false, false, true, false, 0, 0, false, false),  // L2 BTB override
                    GetPerfCtlValue(0x8E, 0, true, true, false, false, true, false, 0, 0, false, false),  // indirect prediction
                    GetPerfCtlValue(0x91, 0, true, true, false, false, true, false, 0, 0, false, false),  // decoder override
                    GetPerfCtlValue(0xD0, 0, true, true, false, false, true, false, 0, 1, false, false)); // retired fused branches
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
                results.overallCounterValues = cpu.GetOverallCounterValues("Retired Branches", "Retired Misp Branches", "L2 BTB Override", "Indirect Prediction", "Decoder Override", "Retired Fused Branches");
                return results;
            }

            public string[] columns = new string[] { "Item", "Active Cycles", "Instructions", "IPC", "BPU Accuracy", "Branch MPKI", "% Branches", "L2 BTB Overrides/Ki", "Indirect Predicts/Ki", "Decoder Overrides/Ki", "% Branches Fused" };

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
                        string.Format("{0:F2}%", 100 * (1 - counterData.ctr1 / counterData.ctr0)), // bpu acc
                        string.Format("{0:F2}", counterData.ctr1 / counterData.aperf * 1000),      // branch mpki
                        string.Format("{0:F2}%", 100 * counterData.ctr0 / counterData.instr),      // % branches
                        string.Format("{0:F2}", 1000 * counterData.ctr2 / counterData.instr),     // l2 btb overrides
                        string.Format("{0:F2}", 1000 * counterData.ctr3 / counterData.instr),     // ita overrides
                        string.Format("{0:F2}", 1000 * counterData.ctr4 / counterData.instr),     // decoder overrides
                        string.Format("{0:F2}%", counterData.ctr5 / counterData.ctr0 * 100) };    // fused branches
            }
        }

        public class FlopsConfig : MonitoringConfig
        {
            private Zen4 cpu;
            public string GetConfigName() { return "AVX/SSE FLOPs"; }

            public FlopsConfig(Zen4 amdCpu) { cpu = amdCpu; }

            public string[] GetColumns() { return columns; }

            public void Initialize()
            {
                ulong merge = GetPerfCtlValue(0xFF, 0, false, false, false, false, true, false, 0, 0xF, false, false);
                cpu.ProgramPerfCounters(GetPerfCtlValue(0x3, 0b1000, true, true, false, false, true, false, 0, 0, false, false), merge, // fma flops, merge
                    GetPerfCtlValue(0x3, 0b111, true, true, false, false, true, false, 0, 0, false, false), merge,  // add/mul/div flops, merge
                    GetPerfCtlValue(0x3, 0b10000, true, true, false, false, true, false, 0, 0, false, false), merge);// bfloat mac flops, merge
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
                results.overallCounterValues = cpu.GetOverallCounterValues("MacFlops", "(merge)", "Mul/Add/Div Flops", "(merge)", "Bfloat Flops", "(merge)");
                return results;
            }

            public string[] columns = new string[] { "Item", "Active Cycles", "Instructions", "IPC",  "Flops", "FMA Flops", "Mul/Add/Div Flops", "Bfloat Flops", "Total FLOPs"};

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
                        FormatLargeNumber(counterData.ctr0 + counterData.ctr2 + counterData.ctr3),
                        FormatLargeNumber(counterData.ctr0),
                        FormatLargeNumber(counterData.ctr2),
                        FormatLargeNumber(counterData.ctr3),
                        FormatLargeNumber(counterData.totalctr0 + counterData.totalctr2 + counterData.totalctr3),
                };    
            }
        }

        public class VecIntConfig : MonitoringConfig
        {
            private Zen4 cpu;
            public string GetConfigName() { return "Vector Integer Ops"; }

            public VecIntConfig(Zen4 amdCpu) { cpu = amdCpu; }

            public string[] GetColumns() { return columns; }

            public void Initialize()
            {
                cpu.ProgramPerfCounters(GetPerfCtlValue(0xD, 0xF0, true, true, false, false, true, false, 0, 0, false, false), // 256-bit int op
                    GetPerfCtlValue(0xD, 0xF, true, true, false, false, true, false, 0, 0, false, false),  // 128-bit int op
                    GetPerfCtlValue(0xB, 0xF, true, true, false, false, true, false, 0, 0, false, false), 0, 0, 0); // mmx ops
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
                results.overallCounterValues = cpu.GetOverallCounterValues("256b int op", "128b int op", "mmx op", "unused", "unused", "unused");
                return results;
            }

            public string[] columns = new string[] { "Item", "Active Cycles", "Instructions", "IPC", "256-bit INT", "128-bit INT", "MMX" };

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
                        FormatLargeNumber(counterData.ctr0), 
                        FormatLargeNumber(counterData.ctr1),
                        FormatLargeNumber(counterData.ctr2)
                        };
            }
        }

        public class FpOpWidthConfig : MonitoringConfig
        {
            private Zen4 cpu;
            public string GetConfigName() { return "FP Op Width"; }

            public FpOpWidthConfig(Zen4 amdCpu) { cpu = amdCpu; }

            public string[] GetColumns() { return columns; }

            public void Initialize()
            {
                cpu.ProgramPerfCounters(GetPerfCtlValue(8, 1, true, true, false, false, true, false, 0, 0, false, false), // x87
                    GetPerfCtlValue(8, 2, true, true, false, false, true, false, 0, 0, false, false),  // MMX
                    GetPerfCtlValue(8, 4, true, true, false, false, true, false, 0, 0, false, false), // scalar
                    GetPerfCtlValue(8, 8, true, true, false, false, true, false, 0, 0, false, false), // 128-bit
                    GetPerfCtlValue(8, 0x10, true, true, false, false, true, false, 0, 0, false, false), // 256-bit
                    GetPerfCtlValue(8, 0x20, true, true, false, false, true, false, 0, 0, false, false)); // 512-bit
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
                results.overallCounterValues = cpu.GetOverallCounterValues("x87", "MMX", "Scalar", "128-bit", "256-bit", "512-bit");
                return results;
            }

            public string[] columns = new string[] { "Item", "Active Cycles", "Instructions", "IPC", "x87", "64-bit MMX", "Scalar SSE/AVX", "128-bit", "256-bit", "512-bit" };

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
                        FormatLargeNumber(counterData.ctr0),
                        FormatLargeNumber(counterData.ctr1),
                        FormatLargeNumber(counterData.ctr2),
                        FormatLargeNumber(counterData.ctr3),
                        FormatLargeNumber(counterData.ctr4),
                        FormatLargeNumber(counterData.ctr5)
                        };
            }
        }

        public class FpPipes : MonitoringConfig
        {
            private Zen4 cpu;
            public string GetConfigName() { return "FP Pipes (undoc)"; }

            public FpPipes(Zen4 amdCpu) { cpu = amdCpu; }

            public string[] GetColumns() { return columns; }

            public void Initialize()
            {
                cpu.ProgramPerfCounters(GetPerfCtlValue(0, 1, true, true, false, false, true, false, 0, 0, false, false), // x87
                    GetPerfCtlValue(0, 2, true, true, false, false, true, false, 0, 0, false, false),  // MMX
                    GetPerfCtlValue(0, 4, true, true, false, false, true, false, 0, 0, false, false), // scalar
                    GetPerfCtlValue(0, 8, true, true, false, false, true, false, 0, 0, false, false), // 128-bit
                    GetPerfCtlValue(0, 0x10, true, true, false, false, true, false, 0, 0, false, false), // 256-bit
                    GetPerfCtlValue(0, 0x20, true, true, false, false, true, false, 0, 0, false, false)); // 512-bit
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

        public class FetchConfig : MonitoringConfig
        {
            private Zen4 cpu;
            public string GetConfigName() { return "Instruction Fetch"; }

            public FetchConfig(Zen4 amdCpu) { cpu = amdCpu; }

            public string[] GetColumns() { return columns; }

            public void Initialize()
            {
                cpu.ProgramPerfCounters(GetPerfCtlValue(0x8E, 0x1F, true, true, false, false, true, false, 0, 0x1, false, false), // IC access
                    GetPerfCtlValue(0x8E, 0x18, true, true, false, false, true, false, 0, 0x1, false, false),  // IC Miss
                    GetPerfCtlValue(0x8F, 0x7, true, true, false, false, true, false, 0, 0x2, false, false),  // OC Access
                    GetPerfCtlValue(0x8F, 0x4, true, true, false, false, true, false, 0, 0x2, false, false),  // OC Miss
                    GetPerfCtlValue(0x84, 0, true, true, false, false, true, false, 0, 0, false, false),  // iTLB miss, L2 iTLB hit
                    GetPerfCtlValue(0x85, 0xF, true, true, false, false, true, false, 0, 0, false, false)); // L2 iTLB miss (page walk)
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
                results.overallCounterValues = cpu.GetOverallCounterValues("IC Access", "IC Miss", "OC Access", "OC Miss", "iTLB Miss L2 iTLB Hit", "Instr Page Walk");
                return results;
            }

            public string[] columns = new string[] { "Item", "Active Cycles", "Instructions", "IPC", 
                "Op$ Hitrate", "Op$ MPKI", "L1i Hitrate", "L1i MPKI", "iTLB MPKI", "L2 iTLB Hitrate", "L2 iTLB MPKI" };

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
                        string.Format("{0:F2}%", 100 * (1 - counterData.ctr3 / counterData.ctr2)),
                        string.Format("{0:F2}", 1000 * counterData.ctr3 / counterData.instr),
                        string.Format("{0:F2}%", 100 * (1 - counterData.ctr1 / counterData.ctr0)),
                        string.Format("{0:F2}", 1000 * counterData.ctr1 / counterData.instr),
                        string.Format("{0:F2}", 1000 * (counterData.ctr4 + counterData.ctr5) / counterData.instr),
                        FormatPercentage(counterData.ctr4, counterData.ctr4 + counterData.ctr5),
                        string.Format("{0:F2}", 1000 * counterData.ctr5 / counterData.instr),
                        };
            }
        }

        public class FrontendOpsConfig : MonitoringConfig
        {
            private Zen4 cpu;
            public string GetConfigName() { return "Ops from Frontend"; }

            public FrontendOpsConfig(Zen4 amdCpu) { cpu = amdCpu; }

            public string[] GetColumns() { return columns; }

            public void Initialize()
            {
                cpu.ProgramPerfCounters(GetPerfCtlValue(0xAA, 0b100, true, true, false, false, true, false, cmask: 1, 0, false, false), // loop buffer active
                    GetPerfCtlValue(0xAA, 1, true, true, false, false, true, false, cmask: 1, 0, false, false),  // decoder active
                    GetPerfCtlValue(0xAA, 0b10, true, true, false, false, true, false, cmask: 1, 0, false, false),  // oc active
                    GetPerfCtlValue(0xAA, 0b100, true, true, false, false, true, false, 0, 0, false, false),  // uop from loop buffer
                    GetPerfCtlValue(0xAA, 1, true, true, false, false, true, false, 0, 0, false, false),  // uop from decoder
                    GetPerfCtlValue(0xAA, 0b10, true, true, false, false, true, false, 0, 0, false, false)); // uop from op cache
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
                results.overallCounterValues = cpu.GetOverallCounterValues("Loop Buffer Active", "Decoder Active", "OC Active", "Loop Buffer Ops", "Decoder Ops", "OC Ops");
                return results;
            }

            public string[] columns = new string[] { "Item", "Active Cycles", "Instructions", "IPC", 
                "Loop Buffer Ops", "Loop Buffer Ops %", "Loop Buffer Active", "Loop Buffer Ops/C",
                "Op Cache Ops", "Op Cache Ops %", "Op Cache Active", "Op Cache Ops/C",
                "Decoder Ops", "Decoder Ops %", "Decoder Active", "Decoder Ops/C"};

            public string GetHelpText()
            {
                return "";
            }

            private string[] computeMetrics(string label, NormalizedCoreCounterData counterData)
            {
                float totalOps = counterData.ctr3 + counterData.ctr4 + counterData.ctr5;
                return new string[] { label,
                        FormatLargeNumber(counterData.aperf),
                        FormatLargeNumber(counterData.instr),
                        string.Format("{0:F2}", counterData.instr / counterData.aperf),

                        // loop buffer
                        FormatLargeNumber(counterData.ctr3),
                        FormatPercentage(counterData.ctr3, totalOps),
                        FormatPercentage(counterData.ctr0, counterData.aperf),
                        string.Format("{0:F2}", counterData.ctr3 / counterData.ctr0),

                        // OC
                        FormatLargeNumber(counterData.ctr5),
                        FormatPercentage(counterData.ctr5, totalOps),
                        FormatPercentage(counterData.ctr2, counterData.aperf),
                        string.Format("{0:F2}", counterData.ctr5 / counterData.ctr2),

                        // Decoder
                        FormatLargeNumber(counterData.ctr4),
                        FormatPercentage(counterData.ctr4, totalOps),
                        FormatPercentage(counterData.ctr1, counterData.aperf),
                        string.Format("{0:F2}", counterData.ctr4 / counterData.ctr1)
                        };
            }
        }

        public class TlbConfig : MonitoringConfig
        {
            private Zen4 cpu;
            public string GetConfigName() { return "DTLB"; }

            public TlbConfig(Zen4 amdCpu) { cpu = amdCpu; }

            public string[] GetColumns() { return columns; }

            public void Initialize()
            {
                cpu.ProgramPerfCounters(GetPerfCtlValue(0x45, 0xFF, true, true, false, false, true, false, 0, 0, false, false), // DTLB Miss
                    GetPerfCtlValue(0x45, 0xF0, true, true, false, false, true, false, 0, 0, false, false),  // L2 TLB miss
                    GetPerfCtlValue(0x78, 0xFF, true, true, false, false, true, false, 0, 0, false, false),  // TLB Flush
                    GetPerfCtlValue(0x29, 0x3, true, true, false, false, true, false, 0, 0, false, false),  // ls dispatch
                    GetPerfCtlValue(0x35, 0, true, true, false, false, true, false, 0, 0, false, false),  // store forward
                    GetPerfCtlValue(0x85, 0xF, true, true, false, false, true, false, 0, 0, false, false)); // L2 iTLB miss (page walk)
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
                results.overallCounterValues = cpu.GetOverallCounterValues("DTLB Miss", "L2 TLB Miss", "TLB Flush", "LS Dispatch", "Store forward", "Unused");
                return results;
            }

            public string[] columns = new string[] { "Item", "Active Cycles", "Instructions", "IPC", "L1 TLB MPKI", "L2 TLB Hitrate", "L2 TLB MPKI" };

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
                        string.Format("{0:F2}", 1000 * (counterData.ctr0 + counterData.ctr1) / counterData.aperf),
                        FormatPercentage(counterData.ctr0 - counterData.ctr1, counterData.ctr0),
                        string.Format("{0:F2}", 1000 * (counterData.ctr1) / counterData.aperf),
                        };
            }
        }

        public class LocksConfig : MonitoringConfig
        {
            private Zen4 cpu;
            public string GetConfigName() { return "LS Locks"; }

            public LocksConfig(Zen4 amdCpu) { cpu = amdCpu; }

            public string[] GetColumns() { return columns; }

            public void Initialize()
            {
                cpu.ProgramPerfCounters(
                    GetPerfCtlValue(0x29, 0b111, true, true, false, false, true, false, 0, 0, false, false), // LS dispatch, load/store/load-op-store
                    GetPerfCtlValue(0x25, 0x1, true, true, false, false, true, false, 0, 0, false, false), // Bus lock
                    GetPerfCtlValue(0x25, 0b10, true, true, false, false, true, false, 0, 0, false, false),  // zen 2 non-speculative lock
                    GetPerfCtlValue(0x25, 0b100, true, true, false, false, true, false, 0, 0, false, false),  // zen 2 speclocklo
                    GetPerfCtlValue(0x25, 0b1000, true, true, false, false, true, false, 0, 0, false, false),  // zen 2 speclockhi
                    GetPerfCtlValue(0x25, 0xFF, true, true, false, false, true, false, 0, 0, false, false)); // all locks?
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

                cpu.ReadPackagePowerCounter();
                results.overallMetrics = computeMetrics("Overall", cpu.NormalizedTotalCounts);
                results.overallCounterValues = cpu.GetOverallCounterValues("LS Dispatch", "Bus lock", "zen2 nonspeclock", "zen 2 speclocklo", "zen 2 speclockhi", "all locks?");
                return results;
            }

            public string[] columns = new string[] { "Item", "Active Cycles", "Instructions", "IPC", "LS Dispatch", "Bus Lock", "(NonSpecLock)", "(SpecLockLo)", "(SpecLockHi)", "(All Locks)" };

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
                        FormatLargeNumber(counterData.ctr0),
                        FormatLargeNumber(counterData.ctr1),
                        FormatLargeNumber(counterData.ctr2),
                        FormatLargeNumber(counterData.ctr3),
                        FormatLargeNumber(counterData.ctr4),
                        FormatLargeNumber(counterData.ctr5)
                        };
            }
        }

        public class SwPrefetchConfig : MonitoringConfig
        {
            private Zen4 cpu;
            public string GetConfigName() { return "Software Prefetch"; }

            public SwPrefetchConfig(Zen4 amdCpu) { cpu = amdCpu; }

            public string[] GetColumns() { return columns; }

            public void Initialize()
            {
                cpu.ProgramPerfCounters(
                    GetPerfCtlValue(0x59, 1, true, true, false, false, true, false, 0, 0, false, false), // sw prefetch from local L2
                    GetPerfCtlValue(0x59, 0b10, true, true, false, false, true, false, 0, 0, false, false), // internal cache
                    GetPerfCtlValue(0x59, 0b100, true, true, false, false, true, false, 0, 0, false, false),  // external cache, same node
                    GetPerfCtlValue(0x59, 0b1000, true, true, false, false, true, false, 0, 0, false, false),  // local mem
                    GetPerfCtlValue(0x52, 0b11, true, true, false, false, true, false, 0, 0, false, false),  // ineffective prefetch
                    GetPerfCtlValue(0x4B, 0b111, true, true, false, false, true, false, 0, 0, false, false)); // prefetch instrs dispatched
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

                cpu.ReadPackagePowerCounter();
                results.overallMetrics = computeMetrics("Overall", cpu.NormalizedTotalCounts);
                results.overallCounterValues = cpu.GetOverallCounterValues("SwPf LclL2", "SwPf IntCache", "SwPf ExtCacheLocal", "SwPf MemIoLocal", "Ineffective Sw Prefetch", "Prefetch Instrs");
                return results;
            }

            public string[] columns = new string[] { "Item", "Active Cycles", "Instructions", "IPC", "SwPf, L2", "SwPf, Intra-CCX", "SwPf, Cross-CCX", "SwPf, Memory", "% SwPf Ineffective", "Ineffective SwPf", "Prefetch Instrs" };

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
                        FormatLargeNumber(64 * counterData.ctr0) + "B/s",
                        FormatLargeNumber(64 * counterData.ctr1) + "B/s",
                        FormatLargeNumber(64 * counterData.ctr2) + "B/s",
                        FormatLargeNumber(64 * counterData.ctr3) + "B/s",
                        string.Format("{0:F2}%", 100 * counterData.ctr4 / counterData.ctr5),
                        FormatLargeNumber(counterData.ctr4),
                        FormatLargeNumber(counterData.ctr5)
                        };
            }
        }

        public class DCFillConfig : MonitoringConfig
        {
            private Zen4 cpu;
            public string GetConfigName() { return "All L1D Fills"; }

            public DCFillConfig(Zen4 amdCpu) { cpu = amdCpu; }

            public string[] GetColumns() { return columns; }

            public void Initialize()
            {
                cpu.ProgramPerfCounters(
                    GetPerfCtlValue(0x44, 1, true, true, false, false, true, false, 0, 0, false, false), // fill from local L2
                    GetPerfCtlValue(0x44, 0b10, true, true, false, false, true, false, 0, 0, false, false), // local ccx
                    GetPerfCtlValue(0x44, 0b10100, true, true, false, false, true, false, 0, 0, false, false),  // external cache
                    GetPerfCtlValue(0x44, 0b1000, true, true, false, false, true, false, 0, 0, false, false),  // local mem
                    GetPerfCtlValue(0x44, 0x40, true, true, false, false, true, false, 0, 0, false, false),  // remote mem
                    GetPerfCtlValue(0x29, 0b111, true, true, false, false, true, false, 0, 0, false, false)); // LS Dispatch
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

                cpu.ReadPackagePowerCounter();
                results.overallMetrics = computeMetrics("Overall", cpu.NormalizedTotalCounts);
                results.overallCounterValues = cpu.GetOverallCounterValues("LclL2", "LocalCCX", "OtherCCX", "DRAM", "FarDram", "LS Dispatch");
                return results;
            }

            public string[] columns = new string[] { "Item", "Active Cycles", "Instructions", "IPC", "L2", "Intra-CCX", "Cross-CCX", "Memory", "Other NUMA Node", "LS Dispatch" };

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
                        FormatLargeNumber(64 * counterData.ctr0) + "B/s",
                        FormatLargeNumber(64 * counterData.ctr1) + "B/s",
                        FormatLargeNumber(64 * counterData.ctr2) + "B/s",
                        FormatLargeNumber(64 * counterData.ctr3) + "B/s",
                        FormatLargeNumber(64 * counterData.ctr4) + "B/s",
                        FormatLargeNumber(counterData.ctr5)
                        };
            }
        }

        public class DemandDCFillConfig : MonitoringConfig
        {
            private Zen4 cpu;
            public string GetConfigName() { return "Demand L1D Fills"; }

            public DemandDCFillConfig(Zen4 amdCpu) { cpu = amdCpu; }

            public string[] GetColumns() { return columns; }

            public void Initialize()
            {
                cpu.ProgramPerfCounters(
                    GetPerfCtlValue(0x43, 1, true, true, false, false, true, false, 0, 0, false, false), // fill from local L2
                    GetPerfCtlValue(0x43, 0b10, true, true, false, false, true, false, 0, 0, false, false), // local ccx
                    GetPerfCtlValue(0x43, 0b10100, true, true, false, false, true, false, 0, 0, false, false),  // external cache
                    GetPerfCtlValue(0x43, 0b1000, true, true, false, false, true, false, 0, 0, false, false),  // local mem
                    GetPerfCtlValue(0x43, 0x40, true, true, false, false, true, false, 0, 0, false, false),  // remote mem
                    GetPerfCtlValue(0x29, 0b111, true, true, false, false, true, false, 0, 0, false, false)); // LS Dispatch
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

                cpu.ReadPackagePowerCounter();
                results.overallMetrics = computeMetrics("Overall", cpu.NormalizedTotalCounts);
                results.overallCounterValues = cpu.GetOverallCounterValues("LclL2", "LocalCCX", "OtherCCX", "DRAM", "FarDram", "LS Dispatch");
                return results;
            }

            public string[] columns = new string[] { "Item", "Active Cycles", "Instructions", "IPC", "L2", "Intra-CCX", "Cross-CCX", "Memory", "Other NUMA Node", "LS Dispatch" };

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
                        FormatLargeNumber(64 * counterData.ctr0) + "B/s",
                        FormatLargeNumber(64 * counterData.ctr1) + "B/s",
                        FormatLargeNumber(64 * counterData.ctr2) + "B/s",
                        FormatLargeNumber(64 * counterData.ctr3) + "B/s",
                        FormatLargeNumber(64 * counterData.ctr4) + "B/s",
                        FormatLargeNumber(counterData.ctr5)
                        };
            }
        }
        public class DispatchStallConfig : MonitoringConfig
        {
            private Zen4 cpu;
            public string GetConfigName() { return "Dispatch Stalls"; }

            public DispatchStallConfig(Zen4 amdCpu) { cpu = amdCpu; }

            public string[] GetColumns() { return columns; }

            public void Initialize()
            {
                cpu.ProgramPerfCounters(
                    GetPerfCtlValue(0xAF, 0b100000, true, true, false, false, true, false, 0, 0, false, false), // ROB full
                    GetPerfCtlValue(0xAE, 1, true, true, false, false, true, false, 0, 0, false, false), // int regs full
                    GetPerfCtlValue(0xAE, 0b100000, true, true, false, false, true, false, 0, 0, false, false),  // fp regs full
                    GetPerfCtlValue(0xAE, 0b10, true, true, false, false, true, false, 0, 0, false, false),  // ldq full
                    GetPerfCtlValue(0xAE, 0b100, true, true, false, false, true, false, 0, 0, false, false),  // stq full
                    GetPerfCtlValue(0xAE, 0b10000, true, true, false, false, true, false, 0, 0, false, false)); // Taken branch buffer full
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

                cpu.ReadPackagePowerCounter();
                results.overallMetrics = computeMetrics("Overall", cpu.NormalizedTotalCounts);
                results.overallCounterValues = cpu.GetOverallCounterValues("RetireToken", "Int Regs Full", "FP Regs Full", "LDQ Full", "STQ Full", "Taken Branch Buffer Full");
                return results;
            }

            public string[] columns = new string[] { "Item", "Active Cycles", "Instructions", "IPC", "ROB Full", "Int Regs Full", "FP Regs Full", "LDQ Full", "STQ Full", "Taken Branch Buffer Full"};

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
                        string.Format("{0:F2}%", 100 * counterData.ctr0 / counterData.aperf),
                        string.Format("{0:F2}%", 100 * counterData.ctr1 / counterData.aperf),
                        string.Format("{0:F2}%", 100 * counterData.ctr2 / counterData.aperf),
                        string.Format("{0:F2}%", 100 * counterData.ctr3 / counterData.aperf),
                        string.Format("{0:F2}%", 100 * counterData.ctr4 / counterData.aperf),
                        string.Format("{0:F2}%", 100 * counterData.ctr5 / counterData.aperf)
                        };
            }
        }
        public class DispatchStallConfig1 : MonitoringConfig
        {
            private Zen4 cpu;
            public string GetConfigName() { return "Dispatch Stalls (Sched)"; }

            public DispatchStallConfig1(Zen4 amdCpu) { cpu = amdCpu; }

            public string[] GetColumns() { return columns; }

            public void Initialize()
            {
                cpu.ProgramPerfCounters(
                    GetPerfCtlValue(0xAF, 1, true, true, false, false, true, false, 0, 0, false, false), // sched 0 full
                    GetPerfCtlValue(0xAF, 0b10, true, true, false, false, true, false, 0, 0, false, false), // sched 1 full
                    GetPerfCtlValue(0xAF, 0b100, true, true, false, false, true, false, 0, 0, false, false),  // sched 2 full
                    GetPerfCtlValue(0xAF, 0b1000, true, true, false, false, true, false, 0, 0, false, false),  // sched 3 full
                    GetPerfCtlValue(0xAE, 0b1000000, true, true, false, false, true, false, 0, 0, false, false),  // fp sched full
                    GetPerfCtlValue(0xAE, 0b10000000, true, true, false, false, true, false, 0, 0, false, false)); // fp flush recovery
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

                cpu.ReadPackagePowerCounter();
                results.overallMetrics = computeMetrics("Overall", cpu.NormalizedTotalCounts);
                results.overallCounterValues = cpu.GetOverallCounterValues("IntSch0", "IntSch1", "IntSch2", "IntSch3", "FpSch", "FpFlush");
                return results;
            }

            public string[] columns = new string[] { "Item", "Active Cycles", "Instructions", "IPC", "Int Sched 0 Full", "Int Sched 1 Full", "Int Sched 2 Full", "Int Sched 3 Full", "FP Sched Full", "FP Flush Recovery" };

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
                        string.Format("{0:F2}%", 100 * counterData.ctr0 / counterData.aperf),
                        string.Format("{0:F2}%", 100 * counterData.ctr1 / counterData.aperf),
                        string.Format("{0:F2}%", 100 * counterData.ctr2 / counterData.aperf),
                        string.Format("{0:F2}%", 100 * counterData.ctr3 / counterData.aperf),
                        string.Format("{0:F2}%", 100 * counterData.ctr4 / counterData.aperf),
                        string.Format("{0:F2}%", 100 * counterData.ctr5 / counterData.aperf)
                        };
            }
        }
        public class L2Config : MonitoringConfig
        {
            private Zen4 cpu;
            public string GetConfigName() { return "L2 Cache"; }

            public L2Config(Zen4 amdCpu) { cpu = amdCpu; }

            public string[] GetColumns() { return columns; }

            public void Initialize()
            {
                cpu.ProgramPerfCounters(
                    GetPerfCtlValue(0x64, 0b111, true, true, false, false, true, false, 0, 0, false, false), // IC fill reqs
                    GetPerfCtlValue(0x64, 0b1, true, true, false, false, true, false, 0, 0, false, false), // IC fill miss
                    GetPerfCtlValue(0x64, 0b11111000, true, true, false, false, true, false, 0, 0, false, false),  // LS read
                    GetPerfCtlValue(0x64, 0b1000, true, true, false, false, true, false, 0, 0, false, false),  // LS read miss
                    GetPerfCtlValue(0x70, 0x1F, true, true, false, false, true, false, 0, 0, false, false),  // L2 Prefetch Hit from L2
                    GetPerfCtlValue(0x70, 0xE0, true, true, false, false, true, false, 0, 0, false, false)); // L2 Prefetch Hit from DC prefetcher
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

                cpu.ReadPackagePowerCounter();
                results.overallMetrics = computeMetrics("Overall", cpu.NormalizedTotalCounts);
                results.overallCounterValues = cpu.GetOverallCounterValues("L2 Code Read", "L2 Code Miss", "L2 Data Read", "L2 Data Miss", "L2 Prefetcher Hits L2", "L1 Prefetcher Hits L2");
                return results;
            }

            public string[] columns = new string[] { "Item", "Active Cycles", "Instructions", "IPC", "Total L2 BW", "L2 Code Hitrate", "L2 Code Hit BW", "L2 Data Hitrate", "L2 Data Hit BW", "DC Prefetcher Hit BW", "L2 Prefetcher Hits" };

            public string GetHelpText()
            {
                return "";
            }

            private string[] computeMetrics(string label, NormalizedCoreCounterData counterData)
            {
                float totalHits = counterData.ctr5 + (counterData.ctr0 - counterData.ctr1) + (counterData.ctr2 - counterData.ctr3);
                return new string[] { label,
                        FormatLargeNumber(counterData.aperf),
                        FormatLargeNumber(counterData.instr),
                        string.Format("{0:F2}", counterData.instr / counterData.aperf),
                        FormatLargeNumber(64 * totalHits) + "B/s",
                        string.Format("{0:F2}%", 100 * (1 - counterData.ctr1 / counterData.ctr0)),
                        FormatLargeNumber(64 * (counterData.ctr0 - counterData.ctr1)) + "B/s",
                        string.Format("{0:F2}%", 100 * (1 - counterData.ctr3 / counterData.ctr2)),
                        FormatLargeNumber(64 * (counterData.ctr2 - counterData.ctr3)) + "B/s",
                        FormatLargeNumber(counterData.ctr5 * 64) + "B/s",
                        FormatLargeNumber(counterData.ctr4)
                        };
            }
        }

        public class MABOccupancyConfig : MonitoringConfig
        {
            private Zen4 cpu;
            public string GetConfigName() { return "MAB Occupancy"; }

            public MABOccupancyConfig(Zen4 amdCpu) { cpu = amdCpu; }

            public string[] GetColumns() { return columns; }

            public void Initialize()
            {
                cpu.ProgramPerfCounters(
                    GetPerfCtlValue(0x5F, 0, true, true, false, false, true, false, 0, 0, false, false),
                    GetPerfCtlValue(0x5F, 0, true, true, false, false, true, false, cmask: 1, 0, false, false),
                    GetPerfCtlValue(0x5F, 0, true, true, false, false, true, false, cmask: 4, 0, false, false),
                    GetPerfCtlValue(0x5F, 0, true, true, false, false, true, false, cmask: 8, 0, false, false),
                    GetPerfCtlValue(0x5F, 0, true, true, false, false, true, false, cmask: 16, 0, false, false),
                    GetPerfCtlValue(0x5F, 0, true, true, false, false, true, false, cmask: 24, 0, false, false));
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

                cpu.ReadPackagePowerCounter();
                results.overallMetrics = computeMetrics("Overall", cpu.NormalizedTotalCounts);
                results.overallCounterValues = cpu.GetOverallCounterValues("Allocated MABs", "1 MAB allocated", "4 MABs allocated", "8 MABs allocated", "16 MABs allocated", "24 MABs allocated");
                return results;
            }

            public string[] columns = new string[] { "Item", "Active Cycles", "Instructions", "IPC", "Avg Misses/C", "Miss Present", ">= 4", ">= 8", ">= 16", "Full" };

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
                        string.Format("{0:F2}", counterData.ctr0 / counterData.aperf),
                        FormatPercentage(counterData.ctr1, counterData.aperf),
                        FormatPercentage(counterData.ctr2, counterData.aperf),
                        FormatPercentage(counterData.ctr3, counterData.aperf),
                        FormatPercentage(counterData.ctr4, counterData.aperf),
                        FormatPercentage(counterData.ctr5, counterData.aperf)
                        };
            }
        }

        public class L1DBw : MonitoringConfig
        {
            private Zen4 cpu;
            public string GetConfigName() { return "L1D BW"; }

            public L1DBw(Zen4 amdCpu) { cpu = amdCpu; }

            public string[] GetColumns() { return columns; }

            public void Initialize()
            {
                cpu.ProgramPerfCounters(
                    GetPerfCtlValue(0x40, 0, true, true, false, false, true, false, cmask: 1, 0, false, false), // dc access cmask 1
                    GetPerfCtlValue(0x40, 0, true, true, false, false, true, false, cmask: 2, 0, false, false), // dc access cmask 2
                    GetPerfCtlValue(0x40, 0, true, true, false, false, true, false, cmask: 3, 0, false, false), // dc access cmask 3
                    GetPerfCtlValue(0x41, 0x3F, true, true, false, false, true, false, 0, 0, false, false),        // lsmaballoc, load/store
                    GetPerfCtlValue(0x41, 0x40, true, true, false, false, true, false, 0, 0, false, false),        // lsmaballoc, hwpf
                    GetPerfCtlValue(0x29, 0b111, true, true, false, false, true, false, 0, 0, false, false)); // ls dispatch
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

                cpu.ReadPackagePowerCounter();
                results.overallMetrics = computeMetrics("Overall", cpu.NormalizedTotalCounts);
                results.overallCounterValues = cpu.GetOverallCounterValues("DC Access cmask 1", "DC Access cmask 2" ,"DC Access cmask 3", "LsMabAlloc ld/st", "LsMabAlloc hwpf", "LS Dispatch");
                return results;
            }

            public string[] columns = new string[] { "Item", "Active Cycles", "Instructions", "IPC", "L1D Active", "2 Accesses", "3 Accesses", "L1D Accesses/C", 
                "L1D Hitrate", "L1D MPKI", "L1D Accesses/LS Dispatch" };

            public string GetHelpText()
            {
                return "";
            }

            private string[] computeMetrics(string label, NormalizedCoreCounterData counterData)
            {
                float l1daccess = (counterData.ctr0 - counterData.ctr1) + (counterData.ctr1 - counterData.ctr2) * 2 + counterData.ctr2 * 3;
                return new string[] { label,
                        FormatLargeNumber(counterData.aperf),
                        FormatLargeNumber(counterData.instr),
                        string.Format("{0:F2}", counterData.instr / counterData.aperf),
                        FormatPercentage(counterData.ctr0, counterData.aperf),
                        FormatPercentage(counterData.ctr1, counterData.aperf),
                        FormatPercentage(counterData.ctr2, counterData.aperf),
                        string.Format("{0:F2}", l1daccess / counterData.aperf),
                        FormatPercentage(l1daccess - (counterData.ctr3 + counterData.ctr4), l1daccess),
                        string.Format("{0:F2}", 1000 * (counterData.ctr3 + counterData.ctr4) / counterData.instr),
                        string.Format("{0:F2}", l1daccess / counterData.ctr5)
                        };
            }
        }
    }
}

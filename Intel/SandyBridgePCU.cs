using PmcReader.Interop;
using System;
using System.Runtime.Remoting.Messaging;
using System.Threading;

namespace PmcReader.Intel
{
    /// <summary>
    /// The uncore from hell?
    /// </summary>
    public class SandyBridgePCU : ModernIntelCpu
    {
        public const uint PCU_MSR_PMON_CTR0 = 0xC36;
        public const uint PCU_MSR_PMON_CTR1 = 0xC37;
        public const uint PCU_MSR_PMON_CTR2 = 0xC38;
        public const uint PCU_MSR_PMON_CTR3 = 0xC39;
        public const uint PCU_MSR_PMON_BOX_FILTER = 0xC34;
        public const uint PCU_MSR_PMON_CTL0 = 0xC30;
        public const uint PCU_MSR_PMON_CTL1 = 0xC31;
        public const uint PCU_MSR_PMON_CTL2 = 0xC32;
        public const uint PCU_MSR_PMON_CTL3 = 0xC33;
        public const uint PCU_MSR_CORE_C6_CTR = 0x3FD; // C6 state (deep sleep, power gated?)
        public const uint PCU_MSR_CORE_C3_CTR = 0x3FC; // C3 state (sleep, clock gated)
        public const uint PCU_MSR_PMON_BOX_CTL = 0xC24;

        // for event occupancy selection
        public const byte C0_OCCUPANCY = 0b01;
        public const byte C3_OCCUPANCY = 0b10;
        public const byte C6_OCCUPANCY = 0b11;

        // PCU runs at fixed 800 MHz
        public const ulong PcuFrequency = 8000000;

        public SandyBridgePCU()
        {
            architectureName = "Sandy Bridge E Power Control Unit";
            monitoringConfigs = new MonitoringConfig[3];
            monitoringConfigs[0] = new VoltageTransitions(this);
            monitoringConfigs[1] = new Limits(this);
            monitoringConfigs[2] = new ChangeAndPhaseShedding(this);
        }

        /// <summary>
        /// Enable and set up power control unit box counters
        /// </summary>
        /// <param name="ctr0">Counter 0 control</param>
        /// <param name="ctr1">Counter 1 control</param>
        /// <param name="ctr2">Counter 2 control</param>
        /// <param name="ctr3">Counter 3 control</param>
        /// <param name="filter">Box filter control</param>
        public void SetupMonitoringSession(ulong ctr0, ulong ctr1, ulong ctr2, ulong ctr3, ulong filter)
        {
            EnableBoxFreeze();
            FreezeBoxCounters();
            Ring0.WriteMsr(PCU_MSR_PMON_CTL0, ctr0);
            Ring0.WriteMsr(PCU_MSR_PMON_CTL1, ctr1);
            Ring0.WriteMsr(PCU_MSR_PMON_CTL2, ctr2);
            Ring0.WriteMsr(PCU_MSR_PMON_CTL3, ctr3);
            Ring0.WriteMsr(PCU_MSR_PMON_BOX_FILTER, filter);
            ClearBoxCounters();
            Ring0.WriteMsr(PCU_MSR_CORE_C3_CTR, 0);
            Ring0.WriteMsr(PCU_MSR_CORE_C6_CTR, 0);
            UnFreezeBoxCounters();
        }

        public void EnableBoxFreeze()
        {
            ulong freezeEnableValue = GetUncoreBoxCtlRegisterValue(false, false, false, true);
            Ring0.WriteMsr(PCU_MSR_PMON_BOX_CTL, freezeEnableValue);
        }

        public void FreezeBoxCounters()
        {
            ulong freezeValue = GetUncoreBoxCtlRegisterValue(false, false, true, true);
            Ring0.WriteMsr(PCU_MSR_PMON_BOX_CTL, freezeValue);
        }

        public void UnFreezeBoxCounters()
        {
            ulong freezeValue = GetUncoreBoxCtlRegisterValue(false, false, false, true);
            Ring0.WriteMsr(PCU_MSR_PMON_BOX_CTL, freezeValue);
        }

        public void ClearBoxCounters()
        {
            ulong freezeValue = GetUncoreBoxCtlRegisterValue(false, true, false, true);
            Ring0.WriteMsr(PCU_MSR_PMON_BOX_CTL, freezeValue);
        }

        /// <summary>
        /// Get value to put in PCU perf counter control registers
        /// </summary>
        /// <param name="perfEvent">PCU event. Bit 7 = use occupancy subcounter?</param>
        /// <param name="occ_sel">Occupancy counter to use</param>
        /// <param name="reset">Reset counter to 0</param>
        /// <param name="edge">Edge detect, must set cmask to >= 1</param>
        /// <param name="extra_select">Extra select bit, undocumented...?</param>
        /// <param name="enable">Enable counter</param>
        /// <param name="invert">Invert cmask, must set cmask to >= 1</param>
        /// <param name="cmask">Counter comparison threshold</param>
        /// <param name="occ_invert">Invert cmask for occupancy events, must set cmask >= 1</param>
        /// <param name="occ_edge">Edge detect for occupancy events, must set cmask >= 1</param>
        /// <returns>Value to put in PCU_MSR_PMON_CTLn</returns>
        public static ulong GetPCUPerfEvtSelRegisterValue(byte perfEvent,
            byte occ_sel,
            bool reset,
            bool edge,
            bool extra_select,
            bool enable,
            bool invert,
            byte cmask,
            bool occ_invert,
            bool occ_edge)
        {
            return perfEvent |
                (ulong)(occ_sel & 0x7) << 14 |
                (reset ? 1UL : 0UL) << 17 |
                (edge ? 1UL : 0UL) << 18 |
                (extra_select ? 1UL : 0UL) << 21 |
                (enable ? 1UL : 0UL) << 22 |
                (invert ? 1UL : 0UL) << 23 |
                (ulong)(cmask & 0xF) << 24 |
                (occ_invert ? 1UL : 0UL) << 30 |
                (occ_edge ? 1UL : 0UL) << 31;
        }

        /// <summary>
        /// Get value to put in PCU_MSR_PMON_BOX_FILTER register
        /// </summary>
        /// <param name="filt7_0">band 0</param>
        /// <param name="filt15_8">band 1</param>
        /// <param name="filt23_16">band 2</param>
        /// <param name="filt31_24">band 3</param>
        /// <returns>PCU box filter register vallue</returns>
        public static ulong GetPCUFilterRegisterValue(byte filt7_0,
            byte filt15_8,
            byte filt23_16,
            byte filt31_24)
        {
            return filt7_0 |
                (ulong)filt15_8 << 8 |
                (ulong)filt23_16 << 16 |
                (ulong)filt31_24 << 24;
        }

        public PcuCounterData ReadPcuCounterData()
        {
            float normalizationFactor = GetNormalizationFactor(0);
            PcuCounterData rc = new PcuCounterData();
            FreezeBoxCounters();
            ulong ctr0, ctr1, ctr2, ctr3;
            Ring0.ReadMsr(PCU_MSR_PMON_CTR0, out ctr0);
            Ring0.ReadMsr(PCU_MSR_PMON_CTR1, out ctr1);
            Ring0.ReadMsr(PCU_MSR_PMON_CTR2, out ctr2);
            Ring0.ReadMsr(PCU_MSR_PMON_CTR3, out ctr3);
            rc.ctr0 = ctr0 * normalizationFactor;
            rc.ctr1 = ctr1 * normalizationFactor;
            rc.ctr2 = ctr2 * normalizationFactor;
            rc.ctr3 = ctr3 * normalizationFactor;
            rc.c3 = ReadAndClearMsr(PCU_MSR_CORE_C3_CTR);
            rc.c6 = ReadAndClearMsr(PCU_MSR_CORE_C6_CTR);
            ClearBoxCounters();
            UnFreezeBoxCounters();
            return rc;
        }

        public class PcuCounterData
        {
            public float ctr0;
            public float ctr1;
            public float ctr2;
            public float ctr3;

            /// <summary>
            /// Cycles some core was in C3 state
            /// </summary>
            public float c3;

            /// <summary>
            /// Cycles some core was in C6 state
            /// </summary>
            public float c6;
        }

        /// <summary>
        /// Get value to put in PMON_BOX_CTL register
        /// </summary>
        /// <param name="rstCtrl">Reset all box control registers to 0</param>
        /// <param name="rstCtrs">Reset all box counter registers to 0</param>
        /// <param name="freeze">Freeze all box counters, if freeze enabled</param>
        /// <param name="freezeEnable">Allow freeze signal</param>
        /// <returns>Value to put in PMON_BOX_CTL register</returns>
        public static ulong GetUncoreBoxCtlRegisterValue(bool rstCtrl,
            bool rstCtrs,
            bool freeze,
            bool freezeEnable)
        {
            return (rstCtrl ? 1UL : 0UL) |
                (rstCtrs ? 1UL : 0UL) << 1 |
                (freeze ? 1UL : 0UL) << 8 |
                (freezeEnable ? 1UL : 0UL) << 16;
        }

        public class VoltageTransitions : MonitoringConfig
        {
            private SandyBridgePCU cpu;
            public string GetConfigName() { return "Voltage Transitions"; }

            public VoltageTransitions(SandyBridgePCU intelCpu)
            {
                cpu = intelCpu;
            }

            public string[] GetColumns()
            {
                return columns;
            }

            public void Initialize()
            {
                ulong voltageIncreaseCycles = GetPCUPerfEvtSelRegisterValue(0x1, 0, reset: false, edge: false, extra_select: false, enable: true, invert: false, cmask: 0, occ_invert: false, occ_edge: false);
                ulong voltageIncreaseCount = GetPCUPerfEvtSelRegisterValue(0x1, 0, reset: false, edge: true, extra_select: false, enable: true, invert: false, cmask: 1, occ_invert: false, occ_edge: false);
                ulong voltageDecreaseCycles = GetPCUPerfEvtSelRegisterValue(0x2, 0, reset: false, edge: false, extra_select: false, enable: true, invert: false, cmask: 0, occ_invert: false, occ_edge: false);;
                ulong voltageDecreaseCount = GetPCUPerfEvtSelRegisterValue(0x2, 0, reset: false, edge: true, extra_select: false, enable: true, invert: false, cmask: 1, occ_invert: false, occ_edge: false);
                ulong filter = 0;
                cpu.SetupMonitoringSession(voltageIncreaseCycles, voltageIncreaseCount, voltageDecreaseCycles, voltageDecreaseCount, filter);
            }

            public MonitoringUpdateResults Update()
            {
                MonitoringUpdateResults results = new MonitoringUpdateResults();
                results.unitMetrics = new string[1][];
                PcuCounterData counterData = cpu.ReadPcuCounterData();

                float increaseLatency = counterData.ctr0 / counterData.ctr1;
                float decreaseLatency = counterData.ctr2 / counterData.ctr3;

                // abuse the form code because there's only one unit and uh, there's vertical space
                results.overallMetrics = new string[] { "Voltage Increase",
                    FormatLargeNumber(counterData.ctr0),
                    FormatLargeNumber(counterData.ctr1),
                    string.Format("{0:F1} clk", increaseLatency),
                    string.Format("{0:F2} ms", increaseLatency * (1000 / (float)PcuFrequency))};
                results.unitMetrics[0] = new string[] { "Voltage Decrease",
                    FormatLargeNumber(counterData.ctr2),
                    FormatLargeNumber(counterData.ctr3),
                    string.Format("{0:F1} clk", decreaseLatency),
                    string.Format("{0:F2} ms", increaseLatency * (1000 / (float)PcuFrequency))};
                return results;
            }

            public string[] columns = new string[] { "Item", "Cycles", "Count", "Latency", "Latency" };
            public string GetHelpText() { return ""; }
        }

        public class Limits : MonitoringConfig
        {
            private SandyBridgePCU cpu;
            public string GetConfigName() { return "Limits"; }

            public Limits(SandyBridgePCU intelCpu)
            {
                cpu = intelCpu;
            }

            public string[] GetColumns()
            {
                return columns;
            }

            public void Initialize()
            {
                ulong thermalLimitCycles = GetPCUPerfEvtSelRegisterValue(0x4, 0, false, false, false, true, false, 0, false, false);
                ulong currentLimitCycles = GetPCUPerfEvtSelRegisterValue(0x7, 0, false, false, false, true, false, 0, false, false);
                ulong osLimitCycles = GetPCUPerfEvtSelRegisterValue(0x6, 0, false, false, false, true, false, 0, false, false);
                ulong powerLimitCycles = GetPCUPerfEvtSelRegisterValue(0x5, 0, false, false, false, true, false, 0, false, false);
                ulong filter = 0;
                cpu.SetupMonitoringSession(thermalLimitCycles, currentLimitCycles, osLimitCycles, powerLimitCycles, filter);
            }

            public MonitoringUpdateResults Update()
            {
                MonitoringUpdateResults results = new MonitoringUpdateResults();
                results.unitMetrics = new string[3][];
                PcuCounterData counterData = cpu.ReadPcuCounterData();

                // abuse the form code because there's only one unit and uh, there's vertical space
                results.overallMetrics = new string[] { "Thermal", string.Format("{0:F2}%", counterData.ctr0 / PcuFrequency) };
                results.unitMetrics[0] = new string[] { "Current", string.Format("{0:F2}%", counterData.ctr1 / PcuFrequency) };
                results.unitMetrics[1] = new string[] { "OS", string.Format("{0:F2}%", counterData.ctr2 / PcuFrequency) };
                results.unitMetrics[2] = new string[] { "Power", string.Format("{0:F2}%", counterData.ctr3 / PcuFrequency) };
                return results;
            }

            public string[] columns = new string[] { "Freq Limit", "Cycles" };
            public string GetHelpText() { return ""; }
        }

        public class ChangeAndPhaseShedding : MonitoringConfig
        {
            private SandyBridgePCU cpu;
            public string GetConfigName() { return "Transition Cycles/Phase Shedding"; }

            public ChangeAndPhaseShedding(SandyBridgePCU intelCpu)
            {
                cpu = intelCpu;
            }

            public string[] GetColumns()
            {
                return columns;
            }

            public void Initialize()
            {
                ulong voltTransCycles = GetPCUPerfEvtSelRegisterValue(0x3, 0, false, false, false, true, false, 0, false, false);
                ulong freqTransCycles = GetPCUPerfEvtSelRegisterValue(0, 0, false, false, extra_select: true, true, false, 0, false, false);
                ulong phaseSheddingCycles = GetPCUPerfEvtSelRegisterValue(0x2F, 0, false, false, false, true, false, 0, false, false);
                ulong cstateTransCycles = GetPCUPerfEvtSelRegisterValue(0xB, 0, false, false, extra_select: true, true, false, 0, false, false);
                ulong filter = 0;
                cpu.SetupMonitoringSession(voltTransCycles, freqTransCycles, cstateTransCycles, phaseSheddingCycles, filter);
            }

            public MonitoringUpdateResults Update()
            {
                MonitoringUpdateResults results = new MonitoringUpdateResults();
                results.unitMetrics = new string[3][];
                PcuCounterData counterData = cpu.ReadPcuCounterData();

                // abuse the form code because there's only one unit and uh, there's vertical space
                results.overallMetrics = new string[] { "Voltage Transition", string.Format("{0:F2}%", counterData.ctr0 / PcuFrequency) };
                results.unitMetrics[0] = new string[] { "Freq Transition", string.Format("{0:F2}%", counterData.ctr1 / PcuFrequency) };
                results.unitMetrics[1] = new string[] { "C State Transition", string.Format("{0:F2}%", counterData.ctr2 / PcuFrequency) };
                results.unitMetrics[2] = new string[] { "Memory Phase Shedding", string.Format("{0:F2}%", counterData.ctr3 / PcuFrequency) };
                return results;
            }

            public string[] columns = new string[] { "Item", "Cycles" };
            public string GetHelpText() { return ""; }
        }
    }
}

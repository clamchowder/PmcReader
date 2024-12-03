using PmcReader.Interop;
using System;

namespace PmcReader.Intel
{
    public class MeteorLakeArb : ModernIntelCpu
    {
        // SNCU and CNCU provide fixed counters for clock ticks
        public const uint MTL_UNC_SNCU_FIXED_CTRL = 0x2002;
        public const uint MTL_UNC_SNCU_FIXED_CTR = 0x2008;
        public const uint MTL_UNC_SNCU_BOX_CTRL = 0x200e;
        public const uint MTL_UNC_CNCU_FIXED_CTRL = 0x2402;
        public const uint MTL_UNC_CNCU_FIXED_CTR = 0x2408;
        public const uint MTL_UNC_CNCU_BOX_CTRL = 0x240e;

        // System agent's arbitration queue?
        public const uint MTL_UNC_ARB_CTRL = 0x2412;
        public const uint MTL_UNC_ARB_CTR = 0x2418;

        // Home agent's arbitration queue? Compute tile -> SoC tile
        public const uint MTL_UNC_HAC_ARB_CTRL = 0x2012;
        public const uint MTL_UNC_HAC_ARB_CTR = 0x2018;

        private ulong lastSncuClk, lastCncuClk;

        public MeteorLakeArb()
        {
            architectureName = "Meteor Lake ARB";
            lastSncuClk = 0;
            monitoringConfigs = new MonitoringConfig[2];
        }

        public class NormalizedArbCounterData
        {
            public float sncuUncoreClk;
            public float cncuUncoreClk;
            public float arbCtr0;
            public float arbCtr1;
            public float hacArbCtr0;
            public float hacArbCtr1;
        }

        public NormalizedArbCounterData UpdateArbCounterData()
        {
            NormalizedArbCounterData rc = new NormalizedArbCounterData();
            float normalizationFactor = GetNormalizationFactor(0);
            ulong sncuClk, cncuClk, elapsedSncuClk, elapsedCncuClk;
            ulong arbCtr0 = ReadAndClearMsr(MTL_UNC_ARB_CTR);
            ulong arbCtr1 = ReadAndClearMsr(MTL_UNC_HAC_ARB_CTR);
            Ring0.ReadMsr(MTL_UNC_SNCU_FIXED_CTR, out sncuClk);
            Ring0.ReadMsr(MTL_UNC_CNCU_FIXED_CTR, out cncuClk);

            // MSR_UNC_PERF_FIXED_CTR is 48 bits wide, upper bits are reserved
            sncuClk &= 0xFFFFFFFFFFFF;
            elapsedSncuClk = sncuClk;
            if (sncuClk > lastSncuClk)
                elapsedSncuClk = sncuClk - lastSncuClk;
            lastSncuClk = sncuClk;

            cncuClk &= 0xFFFFFFFFFFFF;
            elapsedCncuClk = cncuClk;
            if (cncuClk > lastCncuClk)
                elapsedCncuClk = cncuClk - lastCncuClk;
            lastCncuClk = cncuClk;

            rc.arbCtr0 = arbCtr0 * normalizationFactor;
            rc.arbCtr1 = arbCtr1 * normalizationFactor;
            rc.sncuUncoreClk = elapsedSncuClk * normalizationFactor;
            rc.cncuUncoreClk = elapsedCncuClk * normalizationFactor;
            return rc;
        }

        public Tuple<string, float>[] GetOverallCounterValues(NormalizedArbCounterData data, string ctr0, string ctr1)
        {
            Tuple<string, float>[] retval = new Tuple<string, float>[3];
            retval[0] = new Tuple<string, float>("SNCU Clk", data.sncuUncoreClk);
            retval[1] = new Tuple<string, float>("CNCU Clk", data.cncuUncoreClk);
            retval[2] = new Tuple<string, float>(ctr0, data.arbCtr0);
            retval[3] = new Tuple<string, float>(ctr1, data.arbCtr1);
            return retval;
        }
    }
}

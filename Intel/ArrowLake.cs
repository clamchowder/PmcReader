using System;
using System.Collections.Generic;
using PmcReader.Interop;

namespace PmcReader.Intel
{
    public class ArrowLake : ModernIntelCpu
    {
        public static byte ADL_P_CORE_TYPE = 0x40;
        public static byte ADL_E_CORE_TYPE = 0x20;

        public ArrowLake()
        {
            List<MonitoringConfig> configs = new List<MonitoringConfig>();
            architectureName = "Arrow Lake";
            if (coreTypes.Length > 1)
                architectureName += " (Hybrid)";

            // Fix enumeration vs HW support
            for (byte coreIdx = 0; coreIdx < coreTypes.Length; coreIdx++)
            {
                if (coreTypes[coreIdx].Type == ADL_P_CORE_TYPE)
                {
                    coreTypes[coreIdx].Name = "P-Core";
                }
                if (coreTypes[coreIdx].Type == ADL_E_CORE_TYPE)
                {
                    coreTypes[coreIdx].AllocWidth = 8;
                    coreTypes[coreIdx].Name = "E-Core";
                }
            }

            // Create supported configs
            configs.Add(new ArchitecturalCounters(this));
            configs.Add(new RetireHistogram(this));
            for (byte coreIdx = 0; coreIdx < coreTypes.Length; coreIdx++)
            {
                if (coreTypes[coreIdx].Type == ADL_P_CORE_TYPE)
                {

                }
                if (coreTypes[coreIdx].Type == ADL_E_CORE_TYPE)
                {

                }
            }
            monitoringConfigs = configs.ToArray();
        }
    }
}

﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ECore.HardwareInterfaces;
using System.Runtime.InteropServices;

namespace ECore.Devices
{
    partial class ScopeV2
    {
#if INTERNAL
        public
#else
        private
#endif
        struct Calibration
        {
            public AnalogChannel channel;
            public double divider;
            public double multiplier;
            public double[] coefficients;
        }

#if INTERNAL
        public
#else
        private
#endif 
        class Rom
        {
            //number of coefficients per calibration
            const int calibrationSize = 3;
            //Number of possible multiplier/divider combinations
            int modes = validMultipliers.Length * validDividers.Length;
            public ulong plugCount { get; private set; }
            public List<Calibration> calibration { get; private set; }
            ScopeUsbInterface hwInterface;
            public double[] computedMultipliers { get; private set; }
            public double[] computedDividers { get; private set; }

            internal Rom(ScopeUsbInterface hwInterface)
            {
                this.hwInterface = hwInterface;
                Download();
                plugCount++;
                Upload();
            }

            private void computeDividersMultipliers()
            {
                computedMultipliers = new double[validMultipliers.Length];
                computedMultipliers[0] = 1;
                double[] referenceCalibration = getCalibration(AnalogChannel.ChA, validDividers[0], validMultipliers[0]).coefficients;

                for(int i = 1; i < validMultipliers.Length;i++)
                    computedMultipliers[i] = referenceCalibration[0] / getCalibration(AnalogChannel.ChA, validDividers[0], validMultipliers[i]).coefficients[0];

                computedDividers = new double[validDividers.Length];
                computedDividers[0] = 1;
                for (int i = 1; i < validDividers.Length; i++)
                    computedDividers[i] = getCalibration(AnalogChannel.ChA, validDividers[i], validMultipliers[0]).coefficients[0] / referenceCalibration[0];

            }

#if INTERNAL
            public void clearCalibration()
            {
                this.calibration.Clear();
            }

            public void setCalibration(Calibration c)
            {
                if (c.coefficients.Length != calibrationSize)
                    throw new Exception("Coefficients not of correct length!");

                this.calibration.Add(c);
            }
#endif

#if INTERNAL
            public 
#else
	internal
#endif
            Calibration getCalibration(AnalogChannel ch, double divider, double multiplier)
            {
                return calibration.Where(x => x.channel == ch && x.divider == divider && x.multiplier == multiplier).First();
            }

            private byte[] MapToBytes(Map m)
            {
                int size = Marshal.SizeOf(m);
                byte[] output = new byte[size];
                IntPtr p = Marshal.AllocHGlobal(size);

                Marshal.StructureToPtr(m, p, true);
                Marshal.Copy(p, output, 0, size);
                Marshal.FreeHGlobal(p);
                return output;
            }

            private Map BytesToMap(byte[] b)
            {
                Map m = new Map();
                int size = Marshal.SizeOf(m);
                IntPtr ptr = Marshal.AllocHGlobal(size);
                Marshal.Copy(b, 0, ptr, size);

                m = (Map)Marshal.PtrToStructure(ptr, m.GetType());
                Marshal.FreeHGlobal(ptr);

                return m;
            }

            [StructLayout(LayoutKind.Sequential)]
            unsafe struct Map
            {
                public ulong plugCount;
                public fixed float calibration[calibrationSize * 3 * 3 * 2]; //calibrationSize * nDivider * nMultiplier * nChannel
            }

#if INTERNAL
            public
#else
            private 
#endif
            void Upload()
            {
                //Fill ROM map structure
                Map m = new Map();
                m.plugCount = plugCount;
                int offset = 0;
                foreach (AnalogChannel ch in AnalogChannel.list)
                {
                    foreach (double divider in ScopeV2.validDividers)
                    {
                        foreach (double multiplier in ScopeV2.validMultipliers)
                        {
                            double[] coeff = this.calibration.Where(x => x.channel.Value == ch.Value && x.divider == divider && x.multiplier == multiplier).First().coefficients;
                            unsafe
                            {
                                for (int i = 0; i < coeff.Length; i++)
                                    m.calibration[offset + i] = (float)coeff[i];
                            }
                            offset += coeff.Length;
                        }
                    }
                }
                byte[] b = MapToBytes(m);

                int bytesWritten = 0;
                while (bytesWritten < b.Length)
                {
                    int writeLength=Math.Min(12, b.Length - bytesWritten);
                    byte[] tmp = new byte[writeLength];
                    Array.Copy(b, bytesWritten, tmp, 0, writeLength);
                    hwInterface.SetControllerRegister(ScopeController.ROM, bytesWritten, tmp);
                    bytesWritten += writeLength;
                }
            }
#if INTERNAL
            public
#else
            private 
#endif
            void Download()
            {
                int size = Marshal.SizeOf(typeof(Map));
                byte[] romContents = new byte[size];
                int maxReadLength = 12;
                for (int byteOffset = 0; byteOffset < size; )
                {
                    int readLength = Math.Min(maxReadLength, size - byteOffset);
                    byte[] tmp;
                    hwInterface.GetControllerRegister(ScopeController.ROM, byteOffset, readLength, out tmp);
                    Array.Copy(tmp, 0, romContents, byteOffset, readLength);
                    byteOffset += readLength;
                }
                Map m = BytesToMap(romContents);
                this.plugCount = m.plugCount;

                this.calibration = new List<Calibration>();
                int offset = 0;
                foreach (AnalogChannel ch in AnalogChannel.list)
                {
                    foreach (double divider in ScopeV2.validDividers)
                    {
                        foreach (double multiplier in ScopeV2.validMultipliers)
                        {
                            Calibration c = new Calibration()
                            {
                                channel = ch,
                                divider = divider,
                                multiplier = multiplier
                            };
                            double[] coeff = new double[calibrationSize];

                            unsafe
                            {
                                for (int i = 0; i < coeff.Length; i++)
                                    coeff[i] = (double)m.calibration[offset + i];
                            }
                            c.coefficients = coeff;
                            offset += coeff.Length;

                            this.calibration.Add(c);
                        }
                    }
                }
                computeDividersMultipliers();
            }
        }

    }
}
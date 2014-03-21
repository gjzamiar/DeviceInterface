﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

namespace ECore.DeviceImplementations
{
    partial class ScopeV2
    {
        #region flash helpers

        public void FlashPIC2(StreamReader readerStream)
        {
            //PIC18LF14K50_Flasher picFlasher = new PIC18LF14K50_Flasher(eDevice, readerStream);
        }

        public enum PicFlashResult { Success, ReadFromRomFailure, TrialFailedWrongDataReceived, WriteToRomFailure, ErrorParsingHexFile, FailureDuringVerificationReadback }

        public PicFlashResult FlashPIC()
        {
            int i = 0;
            //////////////////////////////////////////////////////////////////////////////////////////////////////////////
            // Convert HEX file into dictionary
            string fileName = "usb.hex";
            StreamReader reader = new StreamReader(fileName);

            Dictionary<uint, byte[]> flashData = new Dictionary<uint, byte[]>();
            uint upperAddress = 0;
            while (!reader.EndOfStream)
            {
                //see http://embeddedfun.blogspot.be/2011/07/anatomy-of-hex-file.html

                string line = reader.ReadLine();
                ushort bytesInThisLine = Convert.ToUInt16(line.Substring(1, 2), 16);
                ushort lowerAddress = Convert.ToUInt16(line.Substring(3, 4), 16);
                ushort contentType = Convert.ToUInt16(line.Substring(7, 2), 16);

                if (contentType == 00) //if this is a data record
                {
                    byte[] bytes = new byte[bytesInThisLine];
                    for (i = 0; i < bytesInThisLine; i++)
                        bytes[i] = Convert.ToByte(line.Substring(9 + i * 2, 2), 16);

                    flashData.Add(upperAddress + lowerAddress, bytes);
                }
                else if (contentType == 04) //contains 2 bytes: the upper address
                {
                    upperAddress = Convert.ToUInt32(line.Substring(9, 4), 16) << 16;
                }
            }
            //////////////////////////////////////////////////////////////////////////////////////////////////////////////

            byte[] sendBytesForUnlock = new byte[] { 123, 5 };
            byte[] sendBytesForFwVersion = new byte[] { 123, 1 };

            //////////////////////////////////////////////////////////////////////////////////////////////////////////////
            //Fetch and print original FW version
            eDevice.DeviceImplementation.hardwareInterface.WriteControlBytes(sendBytesForFwVersion);
            //System.Threading.Thread.Sleep(100);
            byte[] readFwVersion1 = eDevice.DeviceImplementation.hardwareInterface.ReadControlBytes(16);
            Console.Write("Original FW version: ");
            for (i = 2; i < 5; i++)
                Console.Write(readFwVersion1[i].ToString() + ";");
            Console.WriteLine();
            //////////////////////////////////////////////////////////////////////////////////////////////////////////////

            //////////////////////////////////////////////////////////////////////////////////////////////////////////////
            //Try to read from dummy location
            //read 8 bytes from location 0x1FC0
            byte[] sendBytesForRead = new byte[5];
            i = 0;
            sendBytesForRead[i++] = 123;    //preamble
            sendBytesForRead[i++] = 7;      //progRom read
            sendBytesForRead[i++] = 31;     //progRom address MSB
            sendBytesForRead[i++] = 192;    //progRom address LSB
            sendBytesForRead[i++] = 8;      //read 8 bytes

            //send over to HW, to perform read operation
            eDevice.DeviceImplementation.hardwareInterface.WriteControlBytes(sendBytesForRead);

            //now data is stored in EP3 of PIC, so read it
            byte[] readBuffer = eDevice.DeviceImplementation.hardwareInterface.ReadControlBytes(16);
            if (readBuffer.Length != 16) return PicFlashResult.ReadFromRomFailure;
            Console.WriteLine("Trial read successful");
            //////////////////////////////////////////////////////////////////////////////////////////////////////////////


            //////////////////////////////////////////////////////////////////////////////////////////////////////////////
            //Try unlock-erase-write-read on dummy location
            //unlock            
            eDevice.DeviceImplementation.hardwareInterface.WriteControlBytes(sendBytesForUnlock);
            //erase            
            byte[] sendBytesForErase = new byte[] { 123, 9, 31, 192 };
            eDevice.DeviceImplementation.hardwareInterface.WriteControlBytes(sendBytesForErase);
            //write
            byte[] sendBytesForWrite1 = new byte[] { 123, 8, 31, 192, 8, 1, 0, 1, 2, 3, 4, 5, 6, 7 };
            eDevice.DeviceImplementation.hardwareInterface.WriteControlBytes(sendBytesForWrite1);
            byte[] sendBytesForWrite2 = new byte[] { 123, 8, 31, 192, 8, 0, 8, 9, 10, 11, 12, 13, 14, 15 };
            eDevice.DeviceImplementation.hardwareInterface.WriteControlBytes(sendBytesForWrite2);
            //readback
            eDevice.DeviceImplementation.hardwareInterface.WriteControlBytes(sendBytesForRead);
            byte[] readBuffer1 = eDevice.DeviceImplementation.hardwareInterface.ReadControlBytes(16);
            byte[] sendBytesForRead2 = new byte[] { 123, 7, 31, 200, 8 };
            eDevice.DeviceImplementation.hardwareInterface.WriteControlBytes(sendBytesForRead2);
            byte[] readBuffer2 = eDevice.DeviceImplementation.hardwareInterface.ReadControlBytes(16);
            //lock again, in case check crashes
            byte[] sendBytesForLock = new byte[] { 123, 6 };
            //eDevice.DeviceImplementation.hardwareInterface.WriteControlBytes(sendBytesForLock);

            //check
            for (i = 0; i < 8; i++)
                if (readBuffer1[5 + i] != i)
                    return PicFlashResult.TrialFailedWrongDataReceived;
            for (i = 0; i < 8; i++)
                if (readBuffer2[5 + i] != 8 + i)
                    return PicFlashResult.TrialFailedWrongDataReceived;
            Console.WriteLine("Trial erase - write - read successful");
            //////////////////////////////////////////////////////////////////////////////////////////////////////////////

            //////////////////////////////////////////////////////////////////////////////////////////////////////////////
            //Full upper memory erase
            //unlock
            eDevice.DeviceImplementation.hardwareInterface.WriteControlBytes(sendBytesForUnlock);

            //full erase of upper block, done in blocks of 64B at once
            for (i = 0x2000; i < 0x3FFF; i = i + 64)
            {
                byte addressMSB = (byte)(i >> 8);
                byte addressLSB = (byte)i;
                byte[] sendBytesForBlockErase = new byte[] { 123, 9, addressMSB, addressLSB };
                eDevice.DeviceImplementation.hardwareInterface.WriteControlBytes(sendBytesForBlockErase);
                //Console.WriteLine("Erased memblock 0x" + i.ToString("X"));
            }

            //simple check: read data at 0x2000 -- without erase this is never FF
            byte[] sendBytesForRead3 = new byte[] { 123, 7, 0x20, 0, 8 };
            eDevice.DeviceImplementation.hardwareInterface.WriteControlBytes(sendBytesForRead3);
            byte[] readBuffer3 = eDevice.DeviceImplementation.hardwareInterface.ReadControlBytes(16);
            for (i = 0; i < 8; i++)
                if (readBuffer3[5 + i] != 0xFF)
                    return PicFlashResult.TrialFailedWrongDataReceived;
            Console.WriteLine("Upper memory area erased successfuly");
            //////////////////////////////////////////////////////////////////////////////////////////////////////////////

            //////////////////////////////////////////////////////////////////////////////////////////////////////////////
            //Write full memory area with content read from file
            eDevice.DeviceImplementation.hardwareInterface.WriteControlBytes(sendBytesForUnlock);
            //prepare packages
            byte[] writePackage1 = new byte[14];
            byte[] writePackage2 = new byte[14];

            foreach (KeyValuePair<uint, byte[]> kvp in flashData)
            {
                //only flash upper mem area
                if ((kvp.Key >= 0x2000) && (kvp.Key < 0x3FFF))
                {
                    byte[] byteArr = kvp.Value;

                    //fill first packet
                    i = 0;
                    writePackage1[i++] = 123;
                    writePackage1[i++] = 8;
                    writePackage1[i++] = (byte)(kvp.Key >> 8);
                    writePackage1[i++] = (byte)(kvp.Key);
                    writePackage1[i++] = 8;
                    writePackage1[i++] = 1; //first data                    
                    for (i = 0; i < 8; i++)
                        if (byteArr.Length > i)
                            writePackage1[6 + i] = byteArr[i];
                        else
                            writePackage1[6 + i] = 0xEE;

                    //fill second packet
                    i = 0;
                    writePackage2[i++] = 123;
                    writePackage2[i++] = 8;
                    writePackage2[i++] = (byte)(kvp.Key >> 8);
                    writePackage2[i++] = (byte)(kvp.Key);
                    writePackage2[i++] = 8;
                    writePackage2[i++] = 0; //not first data
                    byte[] last8Bytes = new byte[8];
                    for (i = 0; i < 8; i++)
                        if (byteArr.Length > 8 + i)
                            writePackage2[6 + i] = byteArr[8 + i];
                        else
                            writePackage2[6 + i] = 0xFF;

                    //send first packet
                    eDevice.DeviceImplementation.hardwareInterface.WriteControlBytes(writePackage1);
                    //send second packet, including the 16th byte, after which the write actually happens
                    eDevice.DeviceImplementation.hardwareInterface.WriteControlBytes(writePackage2);
                }
            }

            //don't lock here! need to verify memory first.
            //eDevice.DeviceImplementation.hardwareInterface.WriteControlBytes(sendBytesForLock);

            Console.WriteLine("Writing of upper memory area finished");
            //////////////////////////////////////////////////////////////////////////////////////////////////////////////

            //////////////////////////////////////////////////////////////////////////////////////////////////////////////
            //Verify by reading back from PIC memory and comparing to contents from file
            foreach (KeyValuePair<uint, byte[]> kvp in flashData)
            {
                //only flash upper mem area
                if ((kvp.Key >= 0x2000) && (kvp.Key < 0x3FFF))
                {
                    byte[] byteArr = kvp.Value;

                    //read 2 bytes at address
                    byte[] sendBytesForVerificationRead1 = new byte[] { 123, 7, (byte)(kvp.Key >> 8), (byte)kvp.Key, 8 };
                    eDevice.DeviceImplementation.hardwareInterface.WriteControlBytes(sendBytesForVerificationRead1);
                    byte[] readVerificationBytes1 = eDevice.DeviceImplementation.hardwareInterface.ReadControlBytes(16);

                    uint addr = kvp.Key + 8; //need to do this, as there's a possiblity of overflowing
                    byte[] sendBytesForVerificationRead2 = new byte[] { 123, 7, (byte)(addr >> 8), (byte)addr, 8 };
                    eDevice.DeviceImplementation.hardwareInterface.WriteControlBytes(sendBytesForVerificationRead2);
                    byte[] readVerificationBytes2 = eDevice.DeviceImplementation.hardwareInterface.ReadControlBytes(16);

                    //compare
                    for (i = 0; i < 8; i++)
                        if (byteArr.Length > i)
                            if (readVerificationBytes1[5 + i] != byteArr[i])
                                return PicFlashResult.FailureDuringVerificationReadback;
                    for (i = 0; i < 8; i++)
                        if (byteArr.Length > 8 + i)
                            if (readVerificationBytes2[5 + i] != byteArr[8 + i])
                                return PicFlashResult.FailureDuringVerificationReadback;
                }
            }
            Console.WriteLine("Upper area memory validation passed succesfully!");
            //////////////////////////////////////////////////////////////////////////////////////////////////////////////

            //////////////////////////////////////////////////////////////////////////////////////////////////////////////
            //Lock again!
            eDevice.DeviceImplementation.hardwareInterface.WriteControlBytes(sendBytesForLock);

            //and print FW version number to console
            eDevice.DeviceImplementation.hardwareInterface.WriteControlBytes(sendBytesForFwVersion);
            byte[] readFwVersion2 = eDevice.DeviceImplementation.hardwareInterface.ReadControlBytes(16);
            Console.Write("New FW version: ");
            for (i = 2; i < 5; i++)
                Console.Write(readFwVersion2[i].ToString() + ";");
            Console.WriteLine();
            //////////////////////////////////////////////////////////////////////////////////////////////////////////////

            return PicFlashResult.Success;
        }

        //#if IPHONE
        //#else
        public void FlashHW()
        {
            string fileName = "TopEntity_latest.bin";





            List<byte> dataSent = new List<byte>();
            byte[] extendedData = new byte[16] {
				255,
				255,
				255,
				255,
				255,
				255,
				255,
				255,
				255,
				255,
				255,
				255,
				255,
				255,
				255,
				255
			};

            int extendedPacketsToSend = 2048 / 8;

            Stream inStream = null;
            BinaryReader reader = null;
            try
            {
#if ANDROID || IPHONE

				//show all embedded resources
				System.Reflection.Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
				for (int assyIndex = 0; assyIndex < assemblies.Length; assyIndex++) {
					if (reader == null) //dirty patch! otherwise this loop will crash, as there are some assemblies at the end of the list that don't support the following operations and crash
					{
						System.Reflection.Assembly assy = assemblies[assyIndex];
						string[] assetList = assy.GetManifestResourceNames();
						for (int a=0; a<assetList.Length; a++) {
							Logger.AddEntry (this, LogMessageType.Persistent, "ER: " + assetList[a]);
							if (assetList[a].Contains("FPGA_FW_v2.bin"))
							{
								inStream = assy.GetManifestResourceStream(assetList[a]);
								reader = new BinaryReader(inStream);
								Logger.AddEntry (this, LogMessageType.Persistent, "Connected to FW Flash file");
							}
						}
					}	
				}

				//show all assets
				/*string[] assetList2 = Assets.List("");
				for (int a=0; a<assetList2.Length; a++) {
					Logger.AddEntry (this, LogMessageType.Persistent, "Asset: "+assetList2[a]);
				}
				//inStream = Assets.Open(fileName, Android.Content.Res.Access.Streaming);
				//reader = new BinaryReader(inStream);
*/

#else
                inStream = new FileStream(fileName, FileMode.Open);
                reader = new BinaryReader(inStream);
#endif
            }
            catch (Exception e)
            {
                Logger.AddEntry(this, LogMessageType.Persistent, "Opening FPGA FW file failed");
                Logger.AddEntry(this, LogMessageType.Persistent, e.Message);
                return;
            }

            //DemoStatusText = "Entered method";
            if (!eDevice.DeviceImplementation.hardwareInterface.Connected)
            {
                DemoStatusText += " || returning";
                return;
            }

            if (reader == null)
                return;

            try
            {
                //Logger.AddEntry (this, LogMessageType.Persistent, reader.BaseStream.Length.ToString() + " bytes in file");
            }
            catch (Exception e)
            {
                Logger.AddEntry(this, LogMessageType.Persistent, e.Message);
                return;
            }



            ushort fileLength = 0;
            ushort requiredFiller = 0;
            try
            {
                fileLength = (ushort)reader.BaseStream.Length;

                requiredFiller = (ushort)(16 - (fileLength % 16));

                //prep PIC for FPGA flashing
                ushort totalBytesToSend = (ushort)(fileLength + requiredFiller + extendedPacketsToSend * 16);
                byte[] toSend1 = new byte[4];
                int i = 0;
                toSend1[i++] = 123; //message for PIC
                toSend1[i++] = 12; //HOST_COMMAND_FLASH_FPGA
                toSend1[i++] = (byte)(totalBytesToSend >> 8);
                toSend1[i++] = (byte)(totalBytesToSend);

                eDevice.DeviceImplementation.hardwareInterface.WriteControlBytes(toSend1);
            }
            catch
            {
                Logger.AddEntry(this, LogMessageType.Persistent, "Preparing PIC for FPGA flashing failed");
                return;
            }

            //sleep, allowing PIC to erase memory
            System.Threading.Thread.Sleep(10);

            //now send all data in chunks of 16bytes
            ushort bytesSent = 0;
            while ((fileLength - bytesSent) != (16 - requiredFiller))
            {
                byte[] intermediate = reader.ReadBytes(16);
                try
                {
                    eDevice.DeviceImplementation.hardwareInterface.WriteControlBytes(intermediate);
                }
                catch
                {
                    Logger.AddEntry(this, LogMessageType.Persistent, "Writing core FPGA flash data failed");
                    return;
                }

                bytesSent += 16;
                //pb.Value = (int)((float)(bytesSent)/(float)(fileLength)*100f);
                //pb.Update();

                for (int ii = 0; ii < 16; ii++)
                    dataSent.Add(intermediate[ii]);

                DemoStatusText = "Programming FPGA " + bytesSent.ToString();
            }

            //in case filelengt is not multiple of 16: fill with FF
            if (requiredFiller > 0)
            {
                byte[] lastData = new byte[16];
                for (int j = 0; j < 16 - requiredFiller; j++)
                    lastData[j] = reader.ReadByte();
                for (int j = 0; j < requiredFiller; j++)
                    lastData[16 - requiredFiller + j] = 255;
                try
                {
                    eDevice.DeviceImplementation.hardwareInterface.WriteControlBytes(lastData);
                }
                catch
                {
                    Logger.AddEntry(this, LogMessageType.Persistent, "Writing filler failed");
                    return;
                }

                for (int ii = 0; ii < 16; ii++)
                    dataSent.Add(lastData[ii]);

                DemoStatusText = "Sending filler " + requiredFiller.ToString();
            }

            //now send 2048 more packets, allowing the FPGA to boot correctly            
            bytesSent = 0;
            for (int j = 0; j < extendedPacketsToSend; j++)
            {
                try
                {
                    eDevice.DeviceImplementation.hardwareInterface.WriteControlBytes(extendedData);
                }
                catch
                {
                    Logger.AddEntry(this, LogMessageType.Persistent, "Sending extended FW flash data failed");
                    return;
                }
                bytesSent += 16;
                //pb.Value = (int)((float)(bytesSent) / (float)(extendedPacketsToSend * 16) * 100f);
                //pb.Update();

                for (int ii = 0; ii < 16; ii++)
                    dataSent.Add(extendedData[ii]);

                DemoStatusText = "Sending postamp " + j.ToString();
            }

            //close down
            try
            {
                reader.Close();
                inStream.Close();
            }
            catch
            {
                Logger.AddEntry(this, LogMessageType.Persistent, "Closing FPGA FW file failed");
                return;
            }


            DemoStatusText = "";
            /*}
                catch{
                    DemoStatusText = "Error during FPGA programming";
                }*/

        }
        //#endif

        #endregion
    }
}

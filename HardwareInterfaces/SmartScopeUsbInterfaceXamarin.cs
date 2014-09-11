﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Common;
using Android.Hardware.Usb;
using Android.Content;

namespace ECore.HardwareInterfaces
{
    class SmartScopeUsbInterfaceXamarin: ISmartScopeUsbInterface
    {
        private const int COMMAND_READ_ENDPOINT_SIZE = 16;
        private const short COMMAND_WRITE_ENDPOINT_SIZE = 32;
        private UsbEndpoint dataEndpoint;
        private UsbEndpoint commandReadEndpoint;
        private UsbEndpoint commandWriteEndpoint;
        private UsbDeviceConnection usbConnection;

        public SmartScopeUsbInterfaceXamarin(Context context, UsbManager usbManager, UsbDevice device)
        {               
            if(!usbManager.HasPermission(device))
            {
                Logger.Error("Permission denied");
                throw new Exception("Device permission not obtained");
            }
                
            UsbInterface interf = device.GetInterface(0);
            for (int i = 0; i < interf.EndpointCount; i++)
            {
                if (interf.GetEndpoint(i).EndpointNumber == 1)
                    dataEndpoint = interf.GetEndpoint(i);
                else if (interf.GetEndpoint(i).EndpointNumber == 2)
                    commandWriteEndpoint = interf.GetEndpoint(i);
                else if (interf.GetEndpoint(i).EndpointNumber == 3)
                    commandReadEndpoint = interf.GetEndpoint(i);
            }
            usbConnection = usbManager.OpenDevice(device);
            usbConnection.ClaimInterface(interf, true);
        }

        public void WriteControlBytes(byte[] message, bool async)
        {
            if (message.Length > COMMAND_WRITE_ENDPOINT_SIZE)
            {
                throw new ScopeIOException("USB message too long for endpoint");
            }
            WriteControlBytesBulk(message, async);
        }

        public void WriteControlBytesBulk(byte[] message, bool async = false)
        {
            WriteControlBytesBulk(message, 0, message.Length, async);
        }

        public void WriteControlBytesBulk(byte[] message, int offset, int length, bool async = false)
        {
            //try to send data
            try
            {
                byte[] buffer;
                if(offset == 0 && length == message.Length)
                    buffer = message;
                else {
                    buffer = new byte[length];
                    Array.ConstrainedCopy(message, offset, buffer, 0, length);
                }
                usbConnection.BulkTransfer(commandWriteEndpoint, buffer, buffer.Length, 5000);
            }
            catch (Exception ex)
            {
                Logger.Error("Writing control bytes failed" + ex.Message);
            }
        }

        public byte[] ReadControlBytes(int length)
        {
            //try to read data
            try
            {
                //send read command
                byte[] readBuffer = new byte[COMMAND_READ_ENDPOINT_SIZE];
                usbConnection.BulkTransfer(commandReadEndpoint, readBuffer, length, 5000);

                //return read data
                return readBuffer.Take(length).ToArray();
            }
            catch (Exception ex)
            {
                Logger.Error("Reading control bytes failed" + ex.Message);

                return new byte[0];
            }
        }

        public byte[] GetData(int numberOfBytes)
        {
            //see if device is connected properly
            if (dataEndpoint == null)
            {
                Logger.Error("Trying to stream data from device, but dataEndpoint==null");
                return new byte[0];
            }

            //try to read data
            try
            {
                //send read command
                byte[] readBuffer = new byte[numberOfBytes];
                usbConnection.BulkTransfer(dataEndpoint, readBuffer, numberOfBytes, 10000);

                //return read data
                return readBuffer;
            }
            catch (Exception ex)
            {
                Logger.Error("Streaming data from scope failed " + ex.Message);

                return new byte[0];
            }
        }
        public void FlushDataPipe()
        {
            if (dataEndpoint == null)
                throw new ScopeIOException("Data endpoint is null");
            //FIXME: needs implementation
        }

        public string GetSerial()
        {
            return usbConnection.Serial;
        }

        public void Destroy()
        {

        }


    }
}

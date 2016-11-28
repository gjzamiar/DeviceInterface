﻿#define DEBUGCONSOLE

using LabNation.DeviceInterface.Devices;
using LabNation.DeviceInterface.Hardware;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Mono.Zeroconf;
using System.IO;
using System.Net.Sockets;
using System.Net;
using System.Threading;
using LabNation.Common;

namespace LabNation.DeviceInterface.Net
{
    public delegate void ServerChangedHandler(InterfaceServer s, bool connected);

    public class Monitor
    {
        Thread pollThread;
        public List<InterfaceServer> servers = new List<InterfaceServer>();
        private List<IHardwareInterface> hwInterfaces = new List<IHardwareInterface>();

        bool autostart;
        ServerChangedHandler OnServerChanged;
        public Monitor(bool autostart = true, ServerChangedHandler s = null)
        {
            if (s != null)
                OnServerChanged += s;
            this.autostart = autostart;
            //start USB polling thread
            pollThread = new Thread(PollUponStart);
            pollThread.Name = "Devicemanager Startup poll";

#if WINUSB
            InterfaceManagerWinUsb.Instance.onConnect += OnInterfaceConnect;
#else
            InterfaceManagerLibUsb.Instance.onConnect += OnInterfaceConnect;
#endif
            pollThread.Start();
            pollThread.Join();
        }

        public void Stop()
        {
            while(servers.Count > 0)
            {
                InterfaceServer s = servers.First();
                Logger.Info("Stopping server for interface with serial " + s.hwInterface.Serial);
                s.Stop();
            }
        }

        private void PollUponStart()
        {
#if WINUSB
            InterfaceManagerWinUsb.Instance.PollDevice();
#elif !IOS
            InterfaceManagerLibUsb.Instance.PollDevice();
#endif
        }

        private void OnServerDisconnect(InterfaceServer s)
        {
            if(servers.Contains(s))
            {
                servers.Remove(s);
                s.Stop();
                Logger.LogC(LogLevel.INFO, "removed\n", ConsoleColor.Gray);
                if (OnServerChanged != null)
                    OnServerChanged(s, false);
            }

            //If hwInterface remains connected, start new server
            if(hwInterfaces.Contains(s.hwInterface))
                OnInterfaceConnect(s.hwInterface, true);
        }
        private void OnServerStart(InterfaceServer s)
        {
            if (OnServerChanged != null)
                OnServerChanged(s, true);
        }

        private void OnInterfaceConnect(SmartScopeInterfaceUsb hardwareInterface, bool connected)
        {
            if (connected)
            {
                if(!hwInterfaces.Contains(hardwareInterface))
                    hwInterfaces.Add(hardwareInterface);
                Logger.LogC(LogLevel.INFO, "connected\n", ConsoleColor.Gray);
                InterfaceServer s = new InterfaceServer(hardwareInterface);
                servers.Add(s);
                s.OnDisconnect += OnServerDisconnect;
                s.OnStart += OnServerStart;
                if (autostart)
                    s.Start();
                if (OnServerChanged != null)
                    OnServerChanged(s, true);
            }
            else //disconnect
            {
                hwInterfaces.Remove(hardwareInterface);
                InterfaceServer s = servers.Find(x => x.hwInterface == hardwareInterface);
                if (s != null)
                    OnServerDisconnect(s);
            }
        }
    }
}

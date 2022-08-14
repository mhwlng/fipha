using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using HADotNet.Core;
using HADotNet.Core.Clients;
using Microsoft.Win32;

namespace fipha
{
    public class FipHandler
    {
        private List<FipPanel> _fipPanels = new List<FipPanel>();

        private DirectOutputClass.DeviceCallback _deviceCallback;
        private DirectOutputClass.EnumerateCallback _enumerateCallback;
        public bool InitOk;

        //private const String DirectOutputKey = "SOFTWARE\\Saitek\\DirectOutput";

        private const string DirectOutputKey = "SOFTWARE\\Logitech\\DirectOutput"; 

        public bool Initialize()
        {
            InitOk = false;
            try
            {
                _deviceCallback = DeviceCallback;
                _enumerateCallback = EnumerateCallback;

                var key = Registry.LocalMachine.OpenSubKey(DirectOutputKey);

                var value = key?.GetValue("DirectOutput");
                if (value is string)
                {

                    var retVal = DirectOutputClass.Initialize("fipha");
                    if (retVal != ReturnValues.S_OK)
                    {
                        App.Log.Error("FIPHandler failed to init DirectOutputClass. " + retVal);
                        return false;
                    }

                    DirectOutputClass.RegisterDeviceCallback(_deviceCallback);

                    retVal = DirectOutputClass.Enumerate(_enumerateCallback);
                    if (retVal != ReturnValues.S_OK)
                    {
                        App.Log.Error("FIPHandler failed to Enumerate DirectOutputClass. " + retVal);
                        return false;
                    }

                }
            }
            catch (Exception ex)
            {
                App.Log.Error(ex);
                
                return false;
            }
            InitOk = true;
            return true;
        }

        public void Close()
        {
            try
            {
                foreach (var fipPanel in _fipPanels)
                {
                    fipPanel.Shutdown();
                }
                if (InitOk)
                {
                    //No need to deinit if init never worked. (e.g. missing Saitek Drivers)
                    DirectOutputClass.Deinitialize();
                    InitOk = false;
                }
            }
            catch (Exception ex)
            {
                App.Log.Error(ex);
            }
        }

        public void RefreshHAMediaPlayerPages()
        {
            for (var index = 0; index < _fipPanels.Count; index++)
            {
                var fipPanel = _fipPanels[index];

                switch (fipPanel.CurrentTab)
                {
                    case LcdTab.NowPlaying:

                        fipPanel.RefreshDevicePage();
                        break;
                }
            }
        }

        public void RefreshHASensorPages()
        {
            for (var index = 0; index < _fipPanels.Count; index++)
            {
                var fipPanel = _fipPanels[index];

                switch (fipPanel.CurrentTab)
                {
                    case LcdTab.SensorPage1:
                    case LcdTab.SensorPage2:
                    case LcdTab.SensorPage3:
                    case LcdTab.SensorPage4:
                    case LcdTab.SensorPage5:

                        fipPanel.RefreshDevicePage();
                        break;
                }
            }
        }

        private bool IsFipDevice(IntPtr device)
        {
            var mGuid = Guid.Empty;

            DirectOutputClass.GetDeviceType(device, ref mGuid);

            return string.Compare(mGuid.ToString(), "3E083CD8-6A37-4A58-80A8-3D6A2C07513E", true, CultureInfo.InvariantCulture) == 0;
        }


        private void EnumerateCallback(IntPtr device, IntPtr context)
        {
            try
            {
                var mGuid = Guid.Empty;

                DirectOutputClass.GetDeviceType(device, ref mGuid);

                App.Log.Info($"Adding new DirectOutput device {device} of type: {mGuid.ToString()}");

                //Called initially when enumerating FIPs.

                if (!IsFipDevice(device))
                {
                    return;
                }
                var fipPanel = new FipPanel(device);
                _fipPanels.Add(fipPanel);
                fipPanel.Initalize();
            }
            catch (Exception ex)
            {
                App.Log.Error(ex);
            }
        }

        private void DeviceCallback(IntPtr device, bool added, IntPtr context)
        {
            try
            {
                //Called whenever a DirectOutput device is added or removed from the system.
                App.Log.Info("DeviceCallback(): 0x" + device.ToString("x") + (added ? " Added" : " Removed"));

                if (!IsFipDevice(device))
                {
                    return;
                }

                if (!added && _fipPanels.Count == 0)
                {
                    return;
                }

                var i = _fipPanels.Count - 1;
                var found = false;
                do
                {
                    if (_fipPanels[i].FipDevicePointer == device)
                    {
                        found = true;
                        var fipPanel = _fipPanels[i];
                        if (!added)
                        {
                            fipPanel.Shutdown();
                            _fipPanels.Remove(fipPanel);
                        }
                    }
                    i--;
                } while (i >= 0);

                if (added && !found)
                {
                    App.Log.Info("DeviceCallback() Spawning FipPanel. " + device);
                    var fipPanel = new FipPanel(device);
                    _fipPanels.Add(fipPanel);
                    fipPanel.Initalize();
                }
            }
            catch (Exception ex)
            {
                App.Log.Error(ex);
            }

        }

    }
}

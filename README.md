# fip-ha

Home Assistant integration with [Logitech Flight Instrument Panel](https://www.logitechg.com/en-us/products/flight/flight-simulator-instrument-panel.945-000027.html), HWINFO, MQTT

- Now Playing Display for Home Assistant Media Players
- [HWInfo](https://www.hwinfo.com) integration into Home Assistant, via MQTT

**If only the HWINFO integration with Home Assistant is needed, then NO Flight Instrument Panel needs to be connected and no drivers need to be installed.**

If only the Now Playing Display is required, then no HWINFO or MQTT server needs to be set-up or running (in that case, remove mqtt.config).

HWINFO Sensor Entities will be AUTOMATICALLY added to Home Assistant via the MQTT Discovery process.

# Now Playing

![Screenshot 1](https://i.imgur.com/UNOTXH2.jpeg)

The S1 button opens the menu.

The last selected menu option is reloaded at startup.

Use the right rotary encoder to scroll vertically.

On the now playing screen :

Use the left rotary encoder to show another media player.
Also, the S5 button shows the next media player and the S6 button shows the previous media player.

When supported by the mediaplayer: S2,S3,S4 buttons will allow play/pause, next/previous track.

edit appsettings.config with the Home Assistant URL and the Long-Lived Access Token that can be created in the profle screen in Home assistant.

Also players that should be excluded can be defined

```
<?xml version="1.0" encoding="utf-8" ?>
<appSettings>
  <add key="EnableWindowsFormsHighDpiAutoResizing" value="false" />
  <add key="haUrl" value ="http://192.168.2.34:8123/" />
  <add key="haToken" value ="awsdfljhsdjkfhs...........3zFHM" />
  <add key="excludePlayers" value ="media_player.nvidia_shield,media_player.shield_cast,media_player.everywhere,media_player.bedroom_dot,media_player.livingroom_dot"/>
</appSettings>
```

Works with these 64 bit Logitech Flight Instrument Panel Drivers (currently not with older saitek drivers) :

https://support.logi.com/hc/en-us/articles/360024848713--Downloads-Flight-Instrument-Panel

Software Version: 8.0.134.0
Last Update: 2018-01-05
64-bit

https://download01.logi.com/web/ftp/pub/techsupport/simulation/Flight_Instrument_Panel_x64_Drivers_8.0.134.0.exe

# HWINFO

The 'Shared Memory Support' setting in HWInfo must be enabled.

When HWInfo64 is detected, ALL the available sensors will be written at startup to the data\hwinfo.json file.

The HWINFO.inc file must be modified, to configure what will be sent to MQTT.
The HWINFO.inc file has the same format as used by various [rainmeter](https://www.deviantart.com/pul53dr1v3r/art/Rainformer-2-9-3-HWiNFO-Edition-Rainmeter-789616481) skins.

Note that you don't need to install rainmeter or any rainmeter plugin.

A configuration tool, to link sensor ids to variables in the HWINFO.inc file, can be downloaded from the hwinfo website [here](https://www.hwinfo.com/beta/HWiNFOSharedMemoryViewer.exe.7z) :

![hwinfo tool](https://i.imgur.com/Px6jvw4.png)

The HWINFO sensor data can be sent to an MQTT server that is configured in mqtt.config (this file can be deleted if MQTT is not required)

```
<?xml version="1.0" encoding="utf-8" ?>
<configuration>
  <mqtt>
    <add key="mqttURI" value="192.168.2.34" />
    <add key="mqttUser" value="mqttusername" />
    <add key="mqttPassword" value="secretpassword" />
    <add key="mqttPort" value="1883" />
    <add key="mqttSecure" value="False" />
  </mqtt>
</configuration>
```

![MQTT1](https://i.imgur.com/KackkpM.png)

![MQTT2](https://i.imgur.com/p5S3FWw.png)

![MQTT3](https://i.imgur.com/AJBazTy.png)

![MQTT4](https://i.imgur.com/tkaNJDd.png)


# fip-ha

Now Playing Display for Logitech Flight Instrument Panel for Home Assistant Media Players


![Screenshot 1](https://i.imgur.com/xfrzAxM.jpg)

Use the right rotary encoder to scroll vertically.

Use the left rotary encoder to show another media player.
Also, the S5 button shows the next media player and the S6 button shows the previous media player.

edit appsettings.config with the Home Assistant URL and the Long-Lived Access Token that can be created in the profle screen in Home assistant.

```
<?xml version="1.0" encoding="utf-8" ?>
<appSettings>
  <add key="EnableWindowsFormsHighDpiAutoResizing" value="false" />
  <add key="haUrl" value ="http://192.168.2.34:8123/" />
  <add key="haToken" value ="awsdfljhsdjkfhs...........3zFHM" />
</appSettings>
```

Works with these 64 bit Logitech Flight Instrument Panel Drivers (currently not with older saitek drivers) :

https://support.logi.com/hc/en-us/articles/360024848713--Downloads-Flight-Instrument-Panel

Software Version: 8.0.134.0
Last Update: 2018-01-05
64-bit

https://download01.logi.com/web/ftp/pub/techsupport/simulation/Flight_Instrument_Panel_x64_Drivers_8.0.134.0.exe

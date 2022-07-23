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
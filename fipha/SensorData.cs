using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HADotNet.Core.Models;
using System.Drawing;
using System.Globalization;

namespace fipha
{
    public static class SensorData
    {
        
        public class Sensor
        {
            public string EntityId { get; set; }
            public string Name { get; set; }
            public bool Chart = true;
            public int ChartMinutes = 360; // 6 hours default

            [JsonIgnore]
            public string Value { get; set; }
            [JsonIgnore]
            public StateObject State { get; set; }
            [JsonIgnore]
            public HistoryList HistoryList { get; set; }
            [JsonIgnore]
            public string MinVString { get; set; }
            [JsonIgnore]
            public string MaxVString { get; set; }

            [JsonIgnore]
            public PointF[] Points { get; set; }
        }

        public class Section
        {
            public string Name { get; set; }
            public List<Sensor> Sensors { get; set; }
        }
        public class SensorPage
        {
            public string MenuName { get; set; }
            public string CaptionName { get; set; }
            public List<Section> Sections { get; set; }
        }

        public static List<SensorPage> GetSensors(string path)
        {
            try
            {
                path = Path.Combine(App.ExePath, path);

                if (File.Exists(path))
                {
                    return JsonConvert.DeserializeObject<List<SensorPage>>(File.ReadAllText(path));
                }
            }
            catch (Exception ex)
            {
                App.Log.Error(ex);
            }

            return new List<SensorPage>();
        }

        public static (PointF[], string, string) HistoryToChart(SensorData.Sensor sensor, int chartImageDisplayWidth, int chartImageDisplayHeight)
        {
            var startTime = DateTime.UtcNow.AddMinutes(-sensor.ChartMinutes);

            var pixelsPerSecond = chartImageDisplayWidth / (sensor.ChartMinutes * 60.0);

            var minv = (float)1e10;
            var maxv = (float)-1e10;

            var minvString = "";
            var maxvString = "";

            var b = new List<PointF>();

            foreach (var sample in sensor.HistoryList)
            {
                try
                {
                    var y = Convert.ToSingle(sample.State.Replace(",", "."), new CultureInfo("en-US"));

                    if (y < minv)
                    {
                        minv = y;
                        minvString = sample.State;
                    }

                    if (y > maxv)
                    {
                        maxv = y;
                        maxvString = sample.State;
                    }
                }
                catch
                {
                    // do nothing
                }
            }

            var range = maxv - minv;

            if (range > 0)
            {
                minv -= (float)(range * 0.1);
                maxv += (float)(range * 0.1);
            }
            else
            {
                minv -= (float)(maxv * 0.1);
                maxv += (float)(maxv * 0.1);
            }

            if (minv < 0) minv = 0;

            range = maxv - minv;

            if (range > 0)
            {
                var yFactor = chartImageDisplayHeight / range;

                var lastY = (float)0.0;
                foreach (var sample in sensor.HistoryList)
                {
                    try
                    {
                        var y = Convert.ToSingle(sample.State.Replace(",", "."), new CultureInfo("en-US"));
                        lastY = y;

                        var sampleTime = sample.LastChanged;
                        if (sampleTime < startTime)
                        {
                            sampleTime = startTime;
                        }

                        var x = (float)((sampleTime - startTime).TotalSeconds * pixelsPerSecond);

                        b.Add(new PointF(x, chartImageDisplayHeight - (y - minv) * yFactor));
                    }
                    catch
                    {
                        // do nothing
                    }

                }

                var lastX = (float)chartImageDisplayWidth;

                b.Add(new PointF(lastX, chartImageDisplayHeight - (lastY - minv) * yFactor));

            }

            return (b.ToArray(), minvString, maxvString);

        }

    }
}

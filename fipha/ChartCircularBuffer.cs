using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace fipha
{
    public class ChartCircularBuffer
    {
        private readonly LinkedList<float> _buffer = new LinkedList<float>();
        private int _maxItemCount = 1000;
        private HWInfo.SENSOR_TYPE _sensorType;
        private string _unit;
        private float _minv;
        private float _maxv;

        public ChartCircularBuffer(HWInfo.SENSOR_TYPE sensorType, string unit)
        {
            _sensorType = sensorType;
            _unit = unit;
        }

        public void Put(float item)
        {
            lock (_buffer)
            {
                _buffer.AddFirst(item);
                if (_buffer.Count > _maxItemCount)
                {
                    _buffer.RemoveLast();
                }
            }
        }

        public string MinV()
        {
            return _minv == _maxv ? "" : HWInfo.NumberFormat(_sensorType, _unit, _minv);
        }

        public string MaxV()
        {
            return _minv == _maxv ? "" : HWInfo.NumberFormat(_sensorType, _unit, _maxv);
        }

        public PointF[] Read(int chartImageDisplayWidth, int chartImageDisplayHeight)
        {
            lock (_buffer)
            {
                var c = _buffer.Count;
                if (c > chartImageDisplayWidth)
                {
                    c = chartImageDisplayWidth;
                }

                var minv = (float)1e10;
                var maxv = (float)-1e10;

                var b = new PointF[c];

                var node = _buffer.First;

                for (var index = 0; index < c; index++)
                {
                    var y = node.Value;

                    if (y < minv) minv = y;
                    if (y > maxv) maxv = y;

                    node = node.Next;
                }

                var range = maxv - minv;

                if (range > 0)
                {
                    minv -= (float) (range * 0.1);
                    maxv += (float) (range * 0.1);
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

                    node = _buffer.First;

                    for (var index = 0; index < c; index++)
                    {
                        var y = node.Value;

                        var x = chartImageDisplayWidth - 1 - index;

                        b[index] = new PointF(x, chartImageDisplayHeight - (y - minv) * yFactor);

                        node = node.Next;
                    }

                }

                _minv = minv;
                _maxv = maxv;

                return b;
            }
        }
    }
}

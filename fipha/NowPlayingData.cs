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
    public static class NowPlayingData
    {
       
        public class NowPlayingPage
        {
            public string MenuName { get; set; }
            public string CaptionName { get; set; }
            public string EntityId { get; set; }
            [JsonIgnore]
            public StateObject State { get; set; }
        }

        public static List<NowPlayingPage> GetMediaPlayers(string path)
        {
            try
            {
                path = Path.Combine(App.ExePath, path);

                if (File.Exists(path))
                {
                    return JsonConvert.DeserializeObject<List<NowPlayingPage>>(File.ReadAllText(path));
                }
            }
            catch (Exception ex)
            {
                App.Log.Error(ex);
            }

            return new List<NowPlayingPage>();
        }

        
    }
}

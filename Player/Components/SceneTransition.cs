using ContentDistributionPlayer.Extensions;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ContentDistributionPlayer.Components
{
    class SceneTransition
    {
        private const int DEFAULT_DURATION = 200;
        private const string DEFAULT_COLOR = "#000000";

        public class SceneTransitionType
        {
            private SceneTransitionType(string value) { Value = value; }

            public string Value { get; set; }

            public static SceneTransitionType None { get { return new SceneTransitionType("none"); } }
            public static SceneTransitionType SlideToLeft { get { return new SceneTransitionType("slideToLeft"); } }            
        }

        public SceneTransitionType Type { get; set; }
        public Color Color { get; set; }
        public int Duration { get; set; } 

        public static (SceneTransition, string) FromJObject(JObject data)
        {
            if (data == null)
                return (null, @"Scene transition data cannot be empty");

            // get the transition type
            string type = data.Get<string>("type");
            if (type != null)
            {
                SceneTransitionType transType = SceneTransitionType.None;
                if (type == SceneTransitionType.SlideToLeft.Value)
                    transType = SceneTransitionType.SlideToLeft;
                
                if (transType.Value != SceneTransitionType.None.Value)
                {
                    var t = new SceneTransition
                    {
                        Type = transType,
                        Color = ColorTranslator.FromHtml(data.Get<string>("color", DEFAULT_COLOR)),
                        Duration = data.Get<int>("duration", DEFAULT_DURATION)
                    };
                    
                    return (t, null);
                }
            }

            // no transition found
            return (null, null);
        }
    }
}

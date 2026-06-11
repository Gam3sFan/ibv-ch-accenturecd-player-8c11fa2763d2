using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace ContentDistributionPlayer.Utilities
{
    class RuntimeSettingsService
    {
        public string ConfigPath { get; private set; }

        public RuntimeSettingsService(string configPath = null)
        {
            ConfigPath = configPath ?? AppDomain.CurrentDomain.SetupInformation.ConfigurationFile;
        }

        public Dictionary<string, string> ReadAll()
        {
            var values = new Dictionary<string, string>();
            if (!File.Exists(ConfigPath))
                return values;

            XDocument doc = XDocument.Load(ConfigPath);
            foreach (XElement setting in doc.Descendants("setting"))
            {
                string name = (string)setting.Attribute("name");
                XElement valueElement = setting.Element("value");
                if (!string.IsNullOrEmpty(name) && valueElement != null)
                    values[name] = valueElement.Value;
            }
            return values;
        }

        public string Get(string name, string defaultValue = "")
        {
            var values = ReadAll();
            return values.ContainsKey(name) ? values[name] : defaultValue;
        }

        public bool GetBool(string name, bool defaultValue = false)
        {
            return bool.TryParse(Get(name), out bool value) ? value : defaultValue;
        }

        public void Save(Dictionary<string, string> values)
        {
            XDocument doc = XDocument.Load(ConfigPath);
            foreach (var pair in values)
            {
                XElement setting = doc.Descendants("setting").FirstOrDefault(element => (string)element.Attribute("name") == pair.Key);
                if (setting == null)
                    continue;

                XElement value = setting.Element("value");
                if (value == null)
                {
                    value = new XElement("value");
                    setting.Add(value);
                }

                value.Value = pair.Value;
            }

            doc.Save(ConfigPath);
        }
    }
}

using System;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;
using Nivan.Shield.Core;

namespace Nivan.Shield.Services
{
    public sealed class SettingsService
    {
        private readonly string _path;
        private readonly AppLogger _logger;

        public SettingsService(string path, AppLogger logger)
        {
            _path = path;
            _logger = logger;
        }

        public AppSettings Load()
        {
            if (!File.Exists(_path)) return AppSettings.CreateDefault();

            try
            {
                DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(AppSettings));
                using (FileStream stream = File.OpenRead(_path))
                {
                    AppSettings settings = (AppSettings)serializer.ReadObject(stream);
                    settings.Normalize();
                    return settings;
                }
            }
            catch (Exception exception)
            {
                _logger.Warning("Invalid settings were ignored: " + exception.Message);
                return AppSettings.CreateDefault();
            }
        }

        public void Save(AppSettings settings)
        {
            settings.Normalize();
            string temporaryPath = _path + ".tmp";
            DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(AppSettings));
            using (FileStream stream = File.Create(temporaryPath))
            {
                serializer.WriteObject(stream, settings);
                stream.Flush(true);
            }

            if (File.Exists(_path)) File.Replace(temporaryPath, _path, null);
            else File.Move(temporaryPath, _path);
            _logger.Info("Settings saved.");
        }
    }
}

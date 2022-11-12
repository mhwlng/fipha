using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Extensions.ManagedClient;
using MQTTnet.Protocol;

namespace fipha
{
    public static class MQTT
    {
        private static MqttFactory factory = new MqttFactory();
        private static IManagedMqttClient mqttClient = factory.CreateManagedMqttClient();
        private static  string ClientId = Guid.NewGuid().ToString();

        private static string _mqttUri;
        private static string _mqttUser;
        private static string _mqttPassword;
        private static int _mqttPort;
        private static bool _mqttSecure;
        private static int _mqttPollingInterval;

        public static int MqttPollingInterval => _mqttPollingInterval;


        public static async Task<bool> Publish(string channel, string value)
        {
            var message = new MqttApplicationMessageBuilder()
                    .WithTopic(channel)
                    .WithPayload(value)
                    .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                    .WithRetainFlag()
                    .Build();

            try { 
                await mqttClient.EnqueueAsync(message);
            }
            catch (Exception ex)
            {
                App.Log.Error($"MQTT Client : Enqueue Failed", ex);
            }

            return true;
        }

        private static void MqttOnConnectingFailed(ConnectingFailedEventArgs e)
        {
            App.Log.Error($"MQTT Client: Connection Failed", e.Exception);
        }

        private static void MqttOnConnected(MqttClientConnectedEventArgs e)
        {

            App.Log.Info($"MQTT Client: Connected with result: {e.ConnectResult?.ResultCode}");
        }

        private static void MqttOnDisconnected(MqttClientDisconnectedEventArgs e)
        {

            App.Log.Error($"MQTT Client: Connection lost with reason: {e.Reason}.");
        }

        public static void FillConfig()
        {
            if (!File.Exists(Path.Combine(App.ExePath, "mqtt.config"))) return;

            var configMap = new ExeConfigurationFileMap
                { ExeConfigFilename = Path.Combine(App.ExePath, "mqtt.config") };

            var config =
                ConfigurationManager.OpenMappedExeConfiguration(configMap, ConfigurationUserLevel.None);

            var myParamsSection = config.GetSection("mqtt");

            var myParamsSectionRawXml = myParamsSection.SectionInformation.GetRawXml();
            var sectionXmlDoc = new XmlDocument();
            sectionXmlDoc.Load(new StringReader(myParamsSectionRawXml));
            var handler = new NameValueSectionHandler();

            var appSection =
                handler.Create(null, null, sectionXmlDoc.DocumentElement) as NameValueCollection;

            _mqttUri = appSection["mqttURI"];
            _mqttUser = appSection["mqttUser"];
            _mqttPassword = appSection["mqttPassword"];
            _mqttPort = Convert.ToInt32(appSection["mqttPort"]);
            _mqttSecure = appSection["mqttSecure"] == "True";
            _mqttPollingInterval = Convert.ToInt32(appSection["mqttPollingInterval"]);

        }

        public static async Task<bool> Connect()
        {
            if (File.Exists(Path.Combine(App.ExePath, "mqtt.config")))
            {
                var configMap = new ExeConfigurationFileMap
                    {ExeConfigFilename = Path.Combine(App.ExePath, "mqtt.config")};

                var config =
                    ConfigurationManager.OpenMappedExeConfiguration(configMap, ConfigurationUserLevel.None);

                var myParamsSection = config.GetSection("mqtt");

                var myParamsSectionRawXml = myParamsSection.SectionInformation.GetRawXml();
                var sectionXmlDoc = new XmlDocument();
                sectionXmlDoc.Load(new StringReader(myParamsSectionRawXml));
                var handler = new NameValueSectionHandler();

                var appSection =
                    handler.Create(null, null, sectionXmlDoc.DocumentElement) as NameValueCollection;

                _mqttUri = appSection["mqttURI"];
                _mqttUser = appSection["mqttUser"];
                _mqttPassword = appSection["mqttPassword"];
                _mqttPort = Convert.ToInt32(appSection["mqttPort"]);
                _mqttSecure = appSection["mqttSecure"] == "True";
                _mqttPollingInterval = Convert.ToInt32(appSection["mqttPollingInterval"]);

                if (string.IsNullOrEmpty(_mqttUri)) return false;
            }
            else return false;

            var messageBuilder = new MqttClientOptionsBuilder()
              //.WithProtocolVersion(MqttProtocolVersion.V500)
              .WithClientId(ClientId)
              .WithCredentials(_mqttUser, _mqttPassword)
              .WithTcpServer(_mqttUri, _mqttPort)
              .WithCleanSession();

            var options = _mqttSecure
              ? messageBuilder
                .WithTls()
                .Build()
              : messageBuilder
                .Build();

            var managedOptions = new ManagedMqttClientOptionsBuilder()
                .WithAutoReconnectDelay(TimeSpan.FromSeconds(30))
                .WithClientOptions(options)
                .Build();

            try
            {
                mqttClient.ConnectedAsync += e =>
                {
                    MqttOnConnected(e);
                    return Task.CompletedTask;
                };
                mqttClient.DisconnectedAsync += e =>
                {
                    MqttOnDisconnected(e);
                    return Task.CompletedTask;
                };
                mqttClient.ConnectingFailedAsync += e =>
                {
                    MqttOnConnectingFailed(e);
                    return Task.CompletedTask;
                };

                await mqttClient.StartAsync(managedOptions);

            }
            catch (Exception ex)
            {
                App.Log.Error($"MQTT CONNECT FAILED", ex);
            }

            return true;

        }

    }

}

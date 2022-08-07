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
using MQTTnet.Protocol;

namespace fipha
{
    public static class MQTT
    {
        private static MqttFactory factory = new MqttFactory();
        private static IMqttClient mqttClient = factory.CreateMqttClient();
        private static  string ClientId = Guid.NewGuid().ToString();

        private static string mqttURI;
        private static string mqttUser;
        private static string mqttPassword;
        private static int mqttPort;
        private static bool mqttSecure;

        public static async Task<bool> Publish(string channel, string value)
        {
            if (mqttClient.IsConnected == false)
            {
                return false;
            }

            var message = new MqttApplicationMessageBuilder()
                    .WithTopic(channel)
                    .WithPayload(value)
                    //.WithAtMostOnceQoS()
                    .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtMostOnce)
                    //.WithRetainFlag()
                    .Build();

            var result = await mqttClient.PublishAsync(message);
            
            return result.ReasonCode == MqttClientPublishReasonCode.Success;
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

                mqttURI = appSection["mqttURI"];
                mqttUser = appSection["mqttUser"];
                mqttPassword = appSection["mqttPassword"];
                mqttPort = Convert.ToInt32(appSection["mqttPort"]);
                mqttSecure = appSection["mqttSecure"] == "True";

                if (string.IsNullOrEmpty(mqttURI)) return false;
            }
            else return false;

            var messageBuilder = new MqttClientOptionsBuilder()
              .WithClientId(ClientId)
              .WithCredentials(mqttUser, mqttPassword)
              .WithTcpServer(mqttURI, mqttPort)
              .WithCleanSession();

            var options = mqttSecure
              ? messageBuilder
                .WithTls()
                .Build()
              : messageBuilder
                .Build();

            try
            {
                var result = await mqttClient.ConnectAsync(options, CancellationToken.None);

                if (result.ResultCode != MqttClientConnectResultCode.Success)
                {
                    App.Log.Error($"MQTT CONNECT FAILED: {result.ResultCode} {result.ReasonString}");
                }

                return result.ResultCode == MqttClientConnectResultCode.Success;
            }
            catch (Exception ex)
            {
                // ignore this exception
                App.Log.Error($"MQTT CONNECT FAILED", ex);
            }

            return false;

        }

    }

}

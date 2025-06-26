/*
The MIT License (MIT)

Copyright (c) 2018 Giovanni Paolo Vigano'

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
*/

using System.Collections.Generic;
using System.Text;
using SOSXR.EnhancedLogger;
using UnityEngine;
using UnityEngine.UI;


/// <summary>
/// Examples for the M2MQTT library (https://github.com/eclipse/paho.mqtt.m2mqtt),
/// </summary>
namespace M2MqttUnity.Examples
{
    /// <summary>
    ///     Script for testing M2MQTT with a Unity UI
    /// </summary>
    public class MQTTDemo : MQTTUnityClient
    {
        [SerializeField] private string m_topicOne = "sosxr/test/topic/one";
        [SerializeField] private int m_payload = 42;

        [Header("User Interface")]
        [SerializeField] private InputField consoleInputField;
        [SerializeField] private Toggle encryptedToggle;
        [SerializeField] private InputField addressInputField;
        [SerializeField] private InputField portInputField;
        [SerializeField] private Button connectButton;
        [SerializeField] private Button disconnectButton;
        [SerializeField] private Button testPublishButton;
        [SerializeField] private Button clearButton;

        private readonly List<string> eventMessages = new();
        private bool updateUI = false;


        private void OnEnable()
        {
            SetUiMessage("Ready.");
            updateUI = true;
        }


        private void TopicOneCallback(string topic)
        {
            this.Success("Received from: " + topic);
        }


        [ContextMenu(nameof(PublishTopicOne))]
        private void PublishTopicOne()
        {
            Publish(m_topicOne, m_payload.ToString());
        }


        public void SetBrokerAddress(string brokerAddress)
        {
            if (addressInputField && !updateUI)
            {
                BrokerAddress = brokerAddress;
            }
        }


        public void SetBrokerPort(string brokerPort)
        {
            if (portInputField && !updateUI)
            {
                int.TryParse(brokerPort, out BrokerPort);
            }
        }


        public void SetEncrypted(bool isEncrypted)
        {
            IsEncrypted = isEncrypted;
        }


        public void SetUiMessage(string msg)
        {
            if (consoleInputField != null)
            {
                consoleInputField.text = msg;
                updateUI = true;
            }
        }


        public void AddUiMessage(string msg)
        {
            if (consoleInputField != null)
            {
                consoleInputField.text += msg + "\n";
                updateUI = true;
            }
        }


        protected override void OnConnecting()
        {
            base.OnConnecting();
            SetUiMessage("Connecting to broker on " + BrokerAddress + ":" + BrokerPort + "...\n");
        }


        protected override void OnConnected()
        {
            base.OnConnected();
            SetUiMessage("Connected to broker on " + BrokerAddress + "\n");

            SubscribeToTopic(m_topicOne, TopicOneCallback);
        }


        protected override void OnConnectionFailed(string errorMessage)
        {
            AddUiMessage("CONNECTION FAILED! " + errorMessage);
        }


        protected override void OnDisconnected()
        {
            AddUiMessage("Disconnected.");
        }


        protected override void OnConnectionLost()
        {
            AddUiMessage("CONNECTION LOST!");
        }


        private void UpdateUI()
        {
            if (client == null)
            {
                if (connectButton != null)
                {
                    connectButton.interactable = true;
                }

                if (disconnectButton != null)
                {
                    disconnectButton.interactable = false;
                }

                if (testPublishButton != null)
                {
                    testPublishButton.interactable = false;
                }

                return;
            }

            if (testPublishButton != null)
            {
                testPublishButton.interactable = client.IsConnected;
            }

            if (disconnectButton != null)
            {
                disconnectButton.interactable = client.IsConnected;
            }

            if (connectButton != null)
            {
                connectButton.interactable = !client.IsConnected;
            }

            if (addressInputField != null && connectButton != null)
            {
                addressInputField.interactable = connectButton.interactable;
                addressInputField.text = BrokerAddress;
            }

            if (portInputField != null && connectButton != null)
            {
                portInputField.interactable = connectButton.interactable;
                portInputField.text = BrokerPort.ToString();
            }

            if (encryptedToggle != null && connectButton != null)
            {
                encryptedToggle.interactable = connectButton.interactable;
                encryptedToggle.isOn = IsEncrypted;
            }

            if (clearButton != null && connectButton != null)
            {
                clearButton.interactable = connectButton.interactable;
            }

            updateUI = false;
        }


        protected override void DecodeMessage(string topic, byte[] message)
        {
            var msg = Encoding.UTF8.GetString(message);
            Debug.Log("Received: " + msg);
            this.Success($"Received {msg}");
            StoreMessage(msg);
        }


        private void StoreMessage(string eventMsg)
        {
            eventMessages.Add(eventMsg);
        }


        private void ProcessMessage(string msg)
        {
            AddUiMessage("Received: " + msg);
        }


        private void LateUpdate() // Update was already taken
        {
            if (eventMessages.Count > 0)
            {
                foreach (var msg in eventMessages)
                {
                    ProcessMessage(msg);
                }

                eventMessages.Clear();
            }

            if (updateUI)
            {
                UpdateUI();
            }
        }


        private void OnDestroy()
        {
            Disconnect();
        }
    }
}
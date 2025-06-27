/*
The MIT License (MIT)

Copyright (c) 2018 Giovanni Paolo Vigano' && 2025 SOSXR

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
    ///     Showing a simple user interface for the MQTT client.
    /// </summary>
    public class MQTTUI : MQTTClient
    {
        [Header("User Interface")]
        [SerializeField] private InputField consoleInputField;
        [SerializeField] private Toggle encryptedToggle;
        [SerializeField] private InputField addressInputField;
        [SerializeField] private InputField portInputField;
        [SerializeField] private Button connectButton;
        [SerializeField] private Button disconnectButton;
        [SerializeField] private Button testPublishButton;
        [SerializeField] private Button clearButton;


        private void OnEnable()
        {
            OnConnected += HandleConnectedUI;
            OnDisconnected += HandleDisconnectedUI;
        }


        private void HandleConnectedUI()
        {
            SetUIMessage("Ready... \n");
            Subscribe(AddUIMessage);
        }


        private void HandleDisconnectedUI()
        {
            SetUIMessage("Disconnected from broker.\n");
        }


        public void SetUIMessage(string msg = "")
        {
            if (consoleInputField == null)
            {
                return;
            }

            consoleInputField.text = msg;

            this.Verbose("MQTTUI: SetUIMessage: " + msg);
            UpdateUI();
        }


        private void AddUIMessage(string topic, byte[] payload)
        {
            if (consoleInputField == null)
            {
                return;
            }

            var msg = Encoding.UTF8.GetString(payload);

            consoleInputField.text += topic + " : " + msg + "\n";

            this.Verbose("MQTTUI: AddUIMessage: " + msg);

            UpdateUI();
        }


        private void UpdateUI()
        {
            if (disconnectButton != null)
            {
                disconnectButton.interactable = IsConnected;
            }

            if (connectButton != null)
            {
                connectButton.interactable = !IsConnected;
            }

            if (testPublishButton != null)
            {
                testPublishButton.interactable = IsConnected;
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
        }


        private void OnDisable()
        {
            Unsubscribe(AddUIMessage);

            SetUIMessage();

            OnConnected += HandleConnectedUI;
            OnDisconnected += HandleDisconnectedUI;
        }
    }
}
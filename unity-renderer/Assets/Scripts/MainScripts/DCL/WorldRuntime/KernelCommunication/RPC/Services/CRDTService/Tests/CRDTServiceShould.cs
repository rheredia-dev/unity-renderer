using System;
using System.IO;
using System.Threading;
using DCL;
using DCL.CRDT;
using Google.Protobuf;
using KernelCommunication;
using NSubstitute;
using NUnit.Framework;
using RPC;
using rpc_csharp;
using rpc_csharp_test;
using rpc_csharp.transport;
using RPC.Services;
using UnityEngine;
using BinaryWriter = KernelCommunication.BinaryWriter;

namespace Tests
{
    public class CRDTServiceShould
    {
        private RPCContext context;
        private ITransport testClientTransport;
        private RpcServer<RPCContext> rpcServer;
        private CancellationTokenSource testCancellationSource;

        [SetUp]
        public void SetUp()
        {
            context = new RPCContext();

            var (clientTransport, serverTransport) = MemoryTransport.Create();
            rpcServer = new RpcServer<RPCContext>();
            rpcServer.AttachTransport(serverTransport, context);

            rpcServer.SetHandler((port, t, c) =>
            {
                CRDTServiceCodeGen.RegisterService(port, new CRDTServiceImpl());
            });
            testClientTransport = clientTransport;
            testCancellationSource = new CancellationTokenSource();
        }

        [TearDown]
        public void TearDown()
        {
            rpcServer.Dispose();
            testCancellationSource.Cancel();
            testCancellationSource.Dispose();
        }

        [Test]
        public async void ProcessIncomingCRDT()
        {
            TestClient testClient = await TestClient.Create(testClientTransport, CRDTServiceCodeGen.ServiceName);

            var messagingControllersManager = Substitute.For<IMessagingControllersManager>();
            messagingControllersManager.HasScenePendingMessages(Arg.Any<string>()).Returns(false);

            string sceneId = "temptation";
            CRDTMessage crdtMessage = new CRDTMessage()
            {
                key1 = 7693,
                timestamp = 799,
                data = new byte[] { 0, 4, 7, 9, 1, 55, 89, 54 }
            };
            bool messageReceived = false;

            // Check if incoming CRDT is dispatched as scene message
            void OnCrdtMessageReceived(string incommingSceneId, CRDTMessage incommingCrdtMessage)
            {
                Assert.AreEqual(sceneId, incommingSceneId);
                Assert.AreEqual(crdtMessage.key1, incommingCrdtMessage.key1);
                Assert.AreEqual(crdtMessage.timestamp, incommingCrdtMessage.timestamp);
                Assert.IsTrue(AreEqual((byte[])incommingCrdtMessage.data, (byte[])crdtMessage.data));
                messageReceived = true;
            }

            context.crdtContext.CrdtMessageReceived += OnCrdtMessageReceived;
            context.crdtContext.MessagingControllersManager = messagingControllersManager;

            // Simulate client sending `crdtMessage` CRDT
            try
            {
                await testClient.CallProcedure<CRDTResponse>("SendCrdt", new CRDTManyMessages()
                {
                    SceneId = sceneId,
                    Payload = ByteString.CopyFrom(CreateCRDTMessage(crdtMessage))
                });
            }
            catch (Exception e)
            {
                Debug.LogError(e);
            }
            finally
            {
                context.crdtContext.CrdtMessageReceived -= OnCrdtMessageReceived;
            }

            Assert.IsTrue(messageReceived);
        }

        [Test]
        public async void SendCRDTtoScene()
        {
            string scene1 = "temptation1";
            string scene2 = "temptation2";

            CRDTProtocol sceneState1 = new CRDTProtocol();
            CRDTProtocol sceneState2 = new CRDTProtocol();

            CRDTMessage messageToScene1 = sceneState1.Create(1, 34, new byte[] { 1, 0, 2, 45, 67 });
            CRDTMessage messageToScene2 = sceneState2.Create(45, 9, new byte[] { 42, 42, 42, 41 });

            sceneState1.ProcessMessage(messageToScene1);
            sceneState2.ProcessMessage(messageToScene2);

            context.crdtContext.scenesOutgoingCrdts.Add(scene1, sceneState1);
            context.crdtContext.scenesOutgoingCrdts.Add(scene2, sceneState2);

            // Simulate client requesting scene's crdt
            try
            {
                TestClient testClient = await TestClient.Create(testClientTransport, CRDTServiceCodeGen.ServiceName);

                // request for `scene1`
                CRDTManyMessages response1 = await testClient.CallProcedure<CRDTManyMessages>("PullCrdt",
                    new PullCRDTRequest() { SceneId = scene1 });

                var deserializer = CRDTDeserializer.DeserializeBatch(response1.Payload.Memory);
                deserializer.MoveNext();
                CRDTMessage message = (CRDTMessage)deserializer.Current;

                Assert.AreEqual(messageToScene1.key1, message.key1);
                Assert.AreEqual(messageToScene1.timestamp, message.timestamp);
                Assert.IsTrue(AreEqual((byte[])messageToScene1.data, (byte[])message.data));
                Assert.IsFalse(context.crdtContext.scenesOutgoingCrdts.ContainsKey(scene1));

                // request for `scene2`
                CRDTManyMessages response2 = await testClient.CallProcedure<CRDTManyMessages>("PullCrdt",
                    new PullCRDTRequest() { SceneId = scene2 });

                deserializer = CRDTDeserializer.DeserializeBatch(response2.Payload.Memory);
                deserializer.MoveNext();
                message = (CRDTMessage)deserializer.Current;

                Assert.AreEqual(messageToScene2.key1, message.key1);
                Assert.AreEqual(messageToScene2.timestamp, message.timestamp);
                Assert.IsTrue(AreEqual((byte[])messageToScene2.data, (byte[])message.data));
                Assert.IsFalse(context.crdtContext.scenesOutgoingCrdts.ContainsKey(scene2));
            }
            catch (Exception e)
            {
                Debug.LogError(e);
            }
        }

        static byte[] CreateCRDTMessage(CRDTMessage message)
        {
            using (MemoryStream msgStream = new MemoryStream())
            {
                using (BinaryWriter msgWriter = new BinaryWriter(msgStream))
                {
                    KernelBinaryMessageSerializer.Serialize(msgWriter, message);
                    return msgStream.ToArray();
                }
            }
        }

        static bool AreEqual(byte[] a, byte[] b)
        {
            if (a == null && b == null)
                return true;

            if (a == null || b == null)
                return false;

            if (a.Length != b.Length)
                return false;

            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i])
                    return false;
            }

            return true;
        }
    }
}
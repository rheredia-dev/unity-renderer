using System;
using System.Collections.Generic;
using DCL;
using DCL.Controllers;
using DCL.CRDT;
using DCL.ECSRuntime;
using NSubstitute;
using NUnit.Framework;
using RPC.Context;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tests
{
    public class CrdtExecutorsManagerShould
    {
        private Dictionary<string, ICRDTExecutor> crdtExecutors;
        private ECSComponentsManager componentsManager;
        private ISceneController sceneController;
        private IWorldState worldState;
        private CRDTServiceContext rpcCrdtServiceContext;
        private CrdtExecutorsManager executorsManager;
        private ECS7TestUtilsScenesAndEntities testUtils;

        [SetUp]
        public void SetUp()
        {
            crdtExecutors = new Dictionary<string, ICRDTExecutor>();
            componentsManager = new ECSComponentsManager(new Dictionary<int, ECSComponentsFactory.ECSComponentBuilder>());
            rpcCrdtServiceContext = new CRDTServiceContext();
            sceneController = Substitute.For<ISceneController>();
            worldState = Substitute.For<IWorldState>();
            executorsManager = new CrdtExecutorsManager(crdtExecutors, componentsManager, sceneController, worldState, rpcCrdtServiceContext);
            testUtils = new ECS7TestUtilsScenesAndEntities(componentsManager);
        }

        [TearDown]
        public void TearDown()
        {
            testUtils.Dispose();
            DataStore.Clear();
        }

        [Test]
        public void DisposeCorrectly()
        {
            crdtExecutors.Add("1", new CRDTExecutor(testUtils.CreateScene("1"), componentsManager));
            crdtExecutors.Add("2", new CRDTExecutor(testUtils.CreateScene("2"), componentsManager));
            crdtExecutors.Add("3", new CRDTExecutor(testUtils.CreateScene("3"), componentsManager));

            executorsManager.Dispose();
            Assert.AreEqual(0, crdtExecutors.Count);
        }

        [Test]
        public void RemoveCrdtExecutorOnSceneUnload()
        {
            const string sceneId = "temptation";
            IParcelScene scene = testUtils.CreateScene(sceneId);

            crdtExecutors.Add(sceneId, new CRDTExecutor(scene, componentsManager));

            sceneController.OnSceneRemoved += Raise.Event<Action<IParcelScene>>(scene);
            Assert.AreEqual(0, crdtExecutors.Count);
        }

        [Test]
        public void IgnoreCrdtMessageWhenSceneNotLoaded()
        {
            const string sceneId = "temptation";
            rpcCrdtServiceContext.CrdtMessageReceived.Invoke(sceneId, new CRDTMessage());
            LogAssert.Expect(LogType.Error, $"CrdtExecutor not found for sceneId {sceneId}");
        }

        [Test]
        public void ReceiveCrdtMessage()
        {
            const string sceneId = "temptation";
            ECS7TestScene scene = testUtils.CreateScene(sceneId);
            scene.crdtExecutor = null;

            worldState.TryGetScene(sceneId, out Arg.Any<IParcelScene>())
                      .Returns(param =>
                      {
                          param[1] = scene;
                          return true;
                      });

            CRDTMessage crdtMessage = new CRDTMessage()
            {
                timestamp = 1,
                data = new byte[0],
                key1 = 1,
                key2 = 2
            };

            rpcCrdtServiceContext.CrdtMessageReceived.Invoke(sceneId, crdtMessage);
            CRDTMessage crtState = crdtExecutors[sceneId].crdtProtocol.GetState(crdtMessage.key1, crdtMessage.key2);
            AssertCrdtMessageEqual(crdtMessage, crtState);
        }

        [Test]
        public void UseCachedExecutorWhenSeveralMessagesFromSameScene()
        {
            const string sceneId = "temptation";
            ECS7TestScene scene = testUtils.CreateScene(sceneId);
            scene.crdtExecutor = null;

            worldState.TryGetScene(sceneId, out Arg.Any<IParcelScene>())
                      .Returns(param =>
                      {
                          param[1] = scene;
                          return true;
                      });

            CRDTMessage crdtMessage1 = new CRDTMessage()
            {
                timestamp = 1,
                data = new byte[0],
                key1 = 1,
                key2 = 2
            };

            CRDTMessage crdtMessage2 = new CRDTMessage()
            {
                timestamp = 1,
                data = new byte[0],
                key1 = 2,
                key2 = 2
            };

            // Send first message
            rpcCrdtServiceContext.CrdtMessageReceived.Invoke(sceneId, crdtMessage1);
            ICRDTExecutor sceneExecutor = crdtExecutors[sceneId];

            // Clear executors dictionary and make worldState.TryGetScene assert
            crdtExecutors.Clear();
            worldState.TryGetScene(sceneId, out Arg.Any<IParcelScene>())
                      .Returns(param =>
                      {
                          Assert.Fail("this shouldn't be called");
                          return true;
                      });

            // Send second message for same scene
            rpcCrdtServiceContext.CrdtMessageReceived.Invoke(sceneId, crdtMessage2);

            CRDTMessage crtStateMsg1 = sceneExecutor.crdtProtocol.GetState(crdtMessage1.key1, crdtMessage1.key2);
            CRDTMessage crtStateMsg2 = sceneExecutor.crdtProtocol.GetState(crdtMessage2.key1, crdtMessage2.key2);
            AssertCrdtMessageEqual(crdtMessage1, crtStateMsg1);
            AssertCrdtMessageEqual(crdtMessage2, crtStateMsg2);
        }

        static void AssertCrdtMessageEqual(CRDTMessage crdt1, CRDTMessage crdt2)
        {
            Assert.AreEqual(crdt1.timestamp, crdt2.timestamp);
            Assert.AreEqual(crdt1.key1, crdt2.key1);
            Assert.AreEqual(crdt1.key2, crdt2.key2);
            Assert.IsTrue(AreEqual((byte[])crdt1.data, (byte[])crdt2.data));
        }

        static bool AreEqual(byte[] a, byte[] b)
        {
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
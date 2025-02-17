using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Cysharp.Threading.Tasks;
using DCL;
using DCL.Controllers;
using DCL.CRDT;
using DCL.ECS7;
using DCL.ECSComponents;
using DCL.ECSRuntime;
using DCL.Models;
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
using UnityEngine.TestTools;
using BinaryWriter = KernelCommunication.BinaryWriter;
using Environment = DCL.Environment;

namespace Tests
{
    public class ECS7HotReload
    {

        [UnityTest]
        public IEnumerator HotReloadSceneCorrectly()
        {
            yield return UniTask.ToCoroutine(async () =>
            {
                const string SCENE_ID = "temptation";
                const int ENTITY_ID = 500;
                const int COMPONENT_ID = ComponentID.MESH_RENDERER;
                IMessage COMPONENT_DATA = new PBMeshRenderer()
                {
                    Box = new PBMeshRenderer.Types.BoxMesh()
                    {
                        Uvs = { }
                    }
                };

                RPCContext context = DataStore.i.rpcContext.context;

                var (clientTransport, serverTransport) = MemoryTransport.Create();

                RpcServer<RPCContext> rpcServer = new RpcServer<RPCContext>();
                rpcServer.AttachTransport(serverTransport, context);

                rpcServer.SetHandler((port, t, c) =>
                {
                    CRDTServiceCodeGen.RegisterService(port, new CRDTServiceImpl());
                });

                try
                {
                    LoadEnvironment();
                    ECSComponentsManager componentsManager = LoadEcs7Dependencies();

                    context.crdtContext.MessagingControllersManager = Environment.i.messaging.manager;

                    TestClient testClient = await TestClient.Create(clientTransport, CRDTServiceCodeGen.ServiceName);
                    await LoadScene(SCENE_ID).ToCoroutine();

                    IParcelScene scene = Environment.i.world.state.GetScene(SCENE_ID);
                    Assert.NotNull(scene);

                    CRDTMessage crdtCreateBoxMessage = new CRDTMessage()
                    {
                        key1 = ENTITY_ID,
                        key2 = COMPONENT_ID,
                        timestamp = 1,
                        data = ProtoSerialization.Serialize(COMPONENT_DATA)
                    };

                    await testClient.CallProcedure<CRDTResponse>("SendCrdt", new CRDTManyMessages()
                    {
                        SceneId = SCENE_ID,
                        Payload = ByteString.CopyFrom(CreateCRDTMessage(crdtCreateBoxMessage))
                    });

                    IDCLEntity entity = scene.GetEntityById(ENTITY_ID);
                    Assert.NotNull(entity);

                    var component = componentsManager.GetComponent(COMPONENT_ID);
                    Assert.IsTrue(component.HasComponent(scene, entity));

                    // Do hot reload
                    await UnloadScene(SCENE_ID);
                    await LoadScene(SCENE_ID);

                    scene = Environment.i.world.state.GetScene(SCENE_ID);
                    Assert.NotNull(scene);

                    crdtCreateBoxMessage = new CRDTMessage()
                    {
                        key1 = ENTITY_ID,
                        key2 = COMPONENT_ID,
                        timestamp = 1,
                        data = ProtoSerialization.Serialize(COMPONENT_DATA)
                    };

                    await testClient.CallProcedure<CRDTResponse>("SendCrdt", new CRDTManyMessages()
                    {
                        SceneId = SCENE_ID,
                        Payload = ByteString.CopyFrom(CreateCRDTMessage(crdtCreateBoxMessage))
                    });

                    entity = scene.GetEntityById(ENTITY_ID);
                    Assert.NotNull(entity);

                    component = componentsManager.GetComponent(COMPONENT_ID);
                    Assert.IsTrue(component.HasComponent(scene, entity));

                }
                catch (Exception e)
                {
                    throw e;
                }
                finally
                {
                    rpcServer.Dispose();
                    DataStore.Clear();
                }
            });
        }

        private static ECSComponentsManager LoadEcs7Dependencies()
        {
            ISceneController sceneController = Environment.i.world.sceneController;
            Dictionary<string, ICRDTExecutor> crdtExecutors = new Dictionary<string, ICRDTExecutor>(1);

            ECSComponentsFactory componentsFactory = new ECSComponentsFactory();
            ECSComponentsManager componentsManager = new ECSComponentsManager(componentsFactory.componentBuilders);
            var crdtExecutorsManager = new CrdtExecutorsManager(crdtExecutors, componentsManager, sceneController,
                Environment.i.world.state, DataStore.i.rpcContext.context.crdtContext);
            var componentsComposer = new ECS7ComponentsComposer(componentsFactory,
                Substitute.For<IECSComponentWriter>(),
                Substitute.For<IInternalECSComponents>());
            return componentsManager;
        }

        private static void LoadEnvironment()
        {
            ServiceLocator serviceLocator = ServiceLocatorFactory.CreateDefault();
            Environment.Setup(serviceLocator);
            serviceLocator.Initialize();
        }

        private static async UniTask LoadScene(string sceneId)
        {
            LoadParcelScenesMessage.UnityParcelScene scene = new LoadParcelScenesMessage.UnityParcelScene()
            {
                basePosition = new Vector2Int(0, 0),
                parcels = new Vector2Int[] { new Vector2Int(0, 0) },
                id = sceneId
            };

            Environment.i.world.sceneController.LoadParcelScenes(JsonUtility.ToJson(scene));

            var message = new QueuedSceneMessage_Scene
            {
                sceneId = scene.id,
                tag = "",
                type = QueuedSceneMessage.Type.SCENE_MESSAGE,
                method = MessagingTypes.INIT_DONE,
                payload = new Protocol.SceneReady()
            };
            Environment.i.world.sceneController.EnqueueSceneMessage(message);

            await UniTask.WaitWhile(() => Environment.i.messaging.manager.HasScenePendingMessages(sceneId));
        }

        private static async UniTask UnloadScene(string sceneId)
        {
            Environment.i.world.sceneController.UnloadScene(sceneId);
            await UniTask.WaitWhile(() => Environment.i.messaging.manager.hasPendingMessages);
        }

        private static byte[] CreateCRDTMessage(CRDTMessage message)
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
    }
}
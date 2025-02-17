using System;
using System.Collections.Generic;
using DCL;
using DCL.CRDT;

namespace RPC.Context
{
    public class CRDTServiceContext
    {
        public readonly Dictionary<string, CRDTProtocol> scenesOutgoingCrdts = new Dictionary<string, CRDTProtocol>(24);
        public IMessagingControllersManager MessagingControllersManager;
        public Action<string, CRDTMessage> CrdtMessageReceived;
    }
}
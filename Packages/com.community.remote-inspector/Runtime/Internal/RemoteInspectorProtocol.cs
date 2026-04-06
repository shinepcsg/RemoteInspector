using System;
using UnityEngine;

namespace RemoteInspector.Internal
{
    [Serializable]
    internal sealed class SocketEnvelope
    {
        public string type;
        public string requestId;
        public string payloadJson;
    }

    [Serializable]
    internal sealed class EmptyPayload
    {
    }

    [Serializable]
    internal sealed class AuthRequestPayload
    {
        public string password;
    }

    [Serializable]
    internal sealed class AuthResponsePayload
    {
        public bool success;
        public string message;
        public InfoPayload info;
    }

    [Serializable]
    internal sealed class InfoPayload
    {
        public string productName;
        public string displayName;
        public int port;
        public string version;
        public string localUrl;
        public string lanUrl;
        public bool requiresPassword;
        public bool tlsEnabled;
        public bool usingGeneratedCertificate;
        public bool running;
        public int connectedClients;
    }

    [Serializable]
    internal sealed class HierarchyRequestPayload
    {
        public string query;
        public bool includeInactive;
        public bool showHidden;
    }

    [Serializable]
    internal sealed class HierarchyResponsePayload
    {
        public HierarchyNodeDto[] nodes;
    }

    [Serializable]
    internal sealed class HierarchyNodeDto
    {
        public int instanceId;
        public int parentInstanceId;
        public int depth;
        public string name;
        public string sceneName;
        public bool activeSelf;
        public bool activeInHierarchy;
        public bool hidden;
        public string tag;
        public int layer;
    }

    [Serializable]
    internal sealed class InspectorRequestPayload
    {
        public int gameObjectInstanceId;
    }

    [Serializable]
    internal sealed class InspectorResponsePayload
    {
        public int gameObjectInstanceId;
        public string name;
        public string sceneName;
        public bool activeSelf;
        public bool activeInHierarchy;
        public InspectorMemberDto[] gameObjectMembers;
        public InspectorComponentDto[] components;
    }

    [Serializable]
    internal sealed class InspectorComponentDto
    {
        public int instanceId;
        public string typeName;
        public bool enabled;
        public bool canToggleEnabled;
        public bool canDestroy;
        public InspectorMemberDto[] members;
    }

    [Serializable]
    internal sealed class InspectorMemberDto
    {
        public string path;
        public string label;
        public string typeName;
        public string value;
        public bool editable;
        public bool multiline;
        public int depth;
        public string controlHint;
        public string[] options;
        public string referenceTypeName;
        public int referenceInstanceId;
    }

    [Serializable]
    internal sealed class SetMemberRequestPayload
    {
        public int gameObjectInstanceId;
        public int componentInstanceId;
        public string memberPath;
        public string value;
    }

    [Serializable]
    internal sealed class GameObjectOperationPayload
    {
        public int gameObjectInstanceId;
        public int parentInstanceId;
    }

    [Serializable]
    internal sealed class SetActiveRequestPayload
    {
        public int gameObjectInstanceId;
        public bool active;
    }

    [Serializable]
    internal sealed class AddComponentRequestPayload
    {
        public int gameObjectInstanceId;
        public string componentType;
    }

    [Serializable]
    internal sealed class DestroyComponentRequestPayload
    {
        public int componentInstanceId;
    }

    [Serializable]
    internal sealed class AckPayload
    {
        public string message;
        public int instanceId;
    }

    [Serializable]
    internal sealed class LogsResponsePayload
    {
        public RemoteLogEntryDto[] entries;
    }

    [Serializable]
    internal sealed class RemoteLogEntryDto
    {
        public int index;
        public string timestamp;
        public string logType;
        public string message;
        public string stackTrace;
    }

    [Serializable]
    internal sealed class ConsoleRequestPayload
    {
        public string command;
    }

    [Serializable]
    internal sealed class ConsoleResponsePayload
    {
        public bool success;
        public string output;
    }

    [Serializable]
    internal sealed class ErrorPayload
    {
        public string message;
    }

    internal static class RemoteInspectorJson
    {
        public static string ToPayloadJson<T>(T payload)
        {
            return payload == null ? string.Empty : JsonUtility.ToJson(payload);
        }

        public static T FromPayloadJson<T>(string payloadJson) where T : new()
        {
            if (string.IsNullOrWhiteSpace(payloadJson))
            {
                return new T();
            }

            return JsonUtility.FromJson<T>(payloadJson) ?? new T();
        }

        public static SocketEnvelope CreateEnvelope(string type, object payload, string requestId = null)
        {
            return new SocketEnvelope
            {
                type = type,
                requestId = requestId ?? string.Empty,
                payloadJson = payload == null ? string.Empty : JsonUtility.ToJson(payload)
            };
        }
    }
}

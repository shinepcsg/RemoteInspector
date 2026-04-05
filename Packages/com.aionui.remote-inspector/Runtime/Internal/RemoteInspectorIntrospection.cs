using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Aion.RemoteInspector.Internal
{
    internal static class RemoteInspectorIntrospection
    {
        private const int MaxInspectionDepth = 5;
        private const int MaxExpandedCollectionItems = 32;

        private static readonly Dictionary<string, Type> StaticRoots = new(StringComparer.OrdinalIgnoreCase)
        {
            ["time"] = typeof(Time),
            ["screen"] = typeof(Screen),
            ["quality"] = typeof(QualitySettings),
            ["application"] = typeof(Application),
            ["audio"] = typeof(AudioListener),
            ["physics"] = typeof(Physics)
        };

        public static HierarchyResponsePayload BuildHierarchy(HierarchyRequestPayload request)
        {
            var allNodes = new List<NodeRecord>(256);
            var seenRoots = new HashSet<int>();
            foreach (var root in EnumerateRootGameObjects())
            {
                if (!seenRoots.Add(root.GetInstanceID()))
                {
                    continue;
                }

                TraverseHierarchy(root, 0, 0, allNodes);
            }

            var query = request?.query?.Trim() ?? string.Empty;
            var showHidden = request != null && request.showHidden;
            var includeInactive = request != null && request.includeInactive;
            var filtered = new List<HierarchyNodeDto>(allNodes.Count);

            HashSet<int> includedIds = null;
            if (!string.IsNullOrEmpty(query))
            {
                includedIds = new HashSet<int>();
                foreach (var record in allNodes)
                {
                    if (!MatchesQuery(record.GameObject, query))
                    {
                        continue;
                    }

                    var current = record;
                    while (current != null)
                    {
                        if (!includedIds.Add(current.Dto.instanceId))
                        {
                            break;
                        }

                        current = current.Parent;
                    }
                }
            }

            foreach (var record in allNodes)
            {
                if (!showHidden && record.Dto.hidden)
                {
                    continue;
                }

                if (includedIds != null)
                {
                    if (!includedIds.Contains(record.Dto.instanceId))
                    {
                        continue;
                    }
                }
                else if (!includeInactive && !record.Dto.activeInHierarchy)
                {
                    continue;
                }

                filtered.Add(record.Dto);
            }

            return new HierarchyResponsePayload
            {
                nodes = filtered.ToArray()
            };
        }

        public static InspectorResponsePayload BuildInspector(int gameObjectInstanceId)
        {
            var gameObject = FindGameObject(gameObjectInstanceId);
            if (gameObject == null)
            {
                throw new InvalidOperationException($"GameObject [{gameObjectInstanceId}] was not found.");
            }

            var gameObjectMembers = new List<InspectorMemberDto>();
            AddGameObjectMembers(gameObject, gameObjectMembers);

            var components = new List<InspectorComponentDto>();
            foreach (var component in gameObject.GetComponents<Component>())
            {
                if (component == null)
                {
                    continue;
                }

                components.Add(BuildComponent(component));
            }

            return new InspectorResponsePayload
            {
                gameObjectInstanceId = gameObject.GetInstanceID(),
                name = gameObject.name,
                sceneName = gameObject.scene.IsValid() ? gameObject.scene.name : "(No Scene)",
                activeSelf = gameObject.activeSelf,
                activeInHierarchy = gameObject.activeInHierarchy,
                gameObjectMembers = gameObjectMembers.ToArray(),
                components = components.ToArray()
            };
        }

        public static AckPayload SetMember(SetMemberRequestPayload request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (request.componentInstanceId != 0)
            {
                var component = FindComponent(request.componentInstanceId);
                if (component == null)
                {
                    throw new InvalidOperationException($"Component [{request.componentInstanceId}] was not found.");
                }

                if (!TrySetObjectPathValue(component, component.GetType(), request.memberPath, request.value, out var error))
                {
                    throw new InvalidOperationException(error);
                }

                return new AckPayload
                {
                    message = $"Updated {component.GetType().Name}.{request.memberPath}",
                    instanceId = component.GetInstanceID()
                };
            }

            var gameObject = FindGameObject(request.gameObjectInstanceId);
            if (gameObject == null)
            {
                throw new InvalidOperationException($"GameObject [{request.gameObjectInstanceId}] was not found.");
            }

            if (!TrySetObjectPathValue(gameObject, typeof(GameObject), request.memberPath, request.value, out var gameObjectError))
            {
                throw new InvalidOperationException(gameObjectError);
            }

            return new AckPayload
            {
                message = $"Updated {gameObject.name}.{request.memberPath}",
                instanceId = gameObject.GetInstanceID()
            };
        }

        public static AckPayload SetActive(SetActiveRequestPayload request)
        {
            var gameObject = FindGameObject(request.gameObjectInstanceId);
            if (gameObject == null)
            {
                throw new InvalidOperationException($"GameObject [{request.gameObjectInstanceId}] was not found.");
            }

            gameObject.SetActive(request.active);
            return new AckPayload
            {
                message = $"{gameObject.name} active = {request.active}",
                instanceId = gameObject.GetInstanceID()
            };
        }

        public static AckPayload CreateEmpty(GameObjectOperationPayload request)
        {
            var created = new GameObject("GameObject");
            if (request != null && request.parentInstanceId != 0)
            {
                var parent = FindGameObject(request.parentInstanceId);
                if (parent != null)
                {
                    created.transform.SetParent(parent.transform, false);
                }
            }

            return new AckPayload
            {
                message = $"Created {created.name}",
                instanceId = created.GetInstanceID()
            };
        }

        public static AckPayload Duplicate(GameObjectOperationPayload request)
        {
            var gameObject = FindGameObject(request.gameObjectInstanceId);
            if (gameObject == null)
            {
                throw new InvalidOperationException($"GameObject [{request.gameObjectInstanceId}] was not found.");
            }

            var clone = Object.Instantiate(gameObject, gameObject.transform.parent);
            clone.name = gameObject.name;
            clone.transform.SetSiblingIndex(gameObject.transform.GetSiblingIndex() + 1);

            return new AckPayload
            {
                message = $"Duplicated {gameObject.name}",
                instanceId = clone.GetInstanceID()
            };
        }

        public static AckPayload DestroyGameObject(GameObjectOperationPayload request)
        {
            var gameObject = FindGameObject(request.gameObjectInstanceId);
            if (gameObject == null)
            {
                throw new InvalidOperationException($"GameObject [{request.gameObjectInstanceId}] was not found.");
            }

            var name = gameObject.name;
            Object.Destroy(gameObject);
            return new AckPayload
            {
                message = $"Destroyed {name}",
                instanceId = request.gameObjectInstanceId
            };
        }

        public static AckPayload AddComponent(AddComponentRequestPayload request)
        {
            var gameObject = FindGameObject(request.gameObjectInstanceId);
            if (gameObject == null)
            {
                throw new InvalidOperationException($"GameObject [{request.gameObjectInstanceId}] was not found.");
            }

            var type = FindComponentType(request.componentType);
            if (type == null)
            {
                throw new InvalidOperationException($"Component type '{request.componentType}' was not found.");
            }

            var component = gameObject.AddComponent(type);
            return new AckPayload
            {
                message = $"Added {type.Name} to {gameObject.name}",
                instanceId = component.GetInstanceID()
            };
        }

        public static AckPayload DestroyComponent(DestroyComponentRequestPayload request)
        {
            var component = FindComponent(request.componentInstanceId);
            if (component == null)
            {
                throw new InvalidOperationException($"Component [{request.componentInstanceId}] was not found.");
            }

            if (component is Transform)
            {
                throw new InvalidOperationException("Transform cannot be destroyed.");
            }

            var name = component.GetType().Name;
            Object.Destroy(component);
            return new AckPayload
            {
                message = $"Destroyed {name}",
                instanceId = request.componentInstanceId
            };
        }

        public static List<GameObject> FindGameObjectsByPattern(string pattern)
        {
            var results = new List<GameObject>();
            foreach (var root in EnumerateRootGameObjects())
            {
                TraverseForPattern(root, pattern ?? "*", results);
            }

            return results;
        }

        public static string[] GetComponentNames(GameObject gameObject)
        {
            return gameObject == null
                ? Array.Empty<string>()
                : gameObject.GetComponents<Component>()
                    .Where(component => component != null)
                    .Select(component => component.GetType().Name)
                    .ToArray();
        }

        public static bool TryGetPathValue(GameObject gameObject, string memberPath, out object value, out Type valueType, out string error)
        {
            value = null;
            valueType = null;
            error = string.Empty;
            if (gameObject == null)
            {
                error = "GameObject not found.";
                return false;
            }

            return TryGetObjectPathValue(gameObject, memberPath, out value, out valueType, out error);
        }

        public static bool TrySetPathValue(GameObject gameObject, string memberPath, string rawValue, out string error)
        {
            if (gameObject == null)
            {
                error = "GameObject not found.";
                return false;
            }

            return TrySetObjectPathValue(gameObject, typeof(GameObject), memberPath, rawValue, out error);
        }

        public static bool TryGetStaticValue(string staticRoot, string memberPath, out object value, out Type valueType, out string error)
        {
            value = null;
            valueType = null;
            error = string.Empty;
            if (!StaticRoots.TryGetValue(staticRoot ?? string.Empty, out var type))
            {
                error = $"Static root '{staticRoot}' was not found.";
                return false;
            }

            return TryGetPathValueInternal(null, type, memberPath.Split('.'), 0, out value, out valueType, out error);
        }

        public static bool TrySetStaticValue(string staticRoot, string memberPath, string rawValue, out string error)
        {
            if (!StaticRoots.TryGetValue(staticRoot ?? string.Empty, out var type))
            {
                error = $"Static root '{staticRoot}' was not found.";
                return false;
            }

            return TrySetPathValueInternal(null, type, memberPath.Split('.'), 0, rawValue, out _, out error);
        }

        public static bool IsStaticRoot(string staticRoot)
        {
            return StaticRoots.ContainsKey(staticRoot ?? string.Empty);
        }

        public static string FormatValue(object value, Type valueType)
        {
            return ValueCodec.Format(value, valueType);
        }

        public static bool TryParseValue(string rawValue, Type targetType, out object value)
        {
            return ValueCodec.TryConvert(rawValue, targetType, out value);
        }

        public static GameObject FindGameObject(int instanceId)
        {
            if (instanceId == 0)
            {
                return null;
            }

            foreach (var root in EnumerateRootGameObjects())
            {
                var result = FindGameObjectRecursive(root.transform, instanceId);
                if (result != null)
                {
                    return result;
                }
            }

            return null;
        }

        public static Component FindComponent(int instanceId)
        {
            foreach (var root in EnumerateRootGameObjects())
            {
                var components = root.GetComponentsInChildren<Component>(true);
                foreach (var component in components)
                {
                    if (component != null && component.GetInstanceID() == instanceId)
                    {
                        return component;
                    }
                }
            }

            return null;
        }

        private static IEnumerable<GameObject> EnumerateRootGameObjects()
        {
            var seen = new HashSet<int>();
            for (var sceneIndex = 0; sceneIndex < SceneManager.sceneCount; sceneIndex++)
            {
                var scene = SceneManager.GetSceneAt(sceneIndex);
                if (!scene.IsValid() || !scene.isLoaded)
                {
                    continue;
                }

                foreach (var root in scene.GetRootGameObjects())
                {
                    if (seen.Add(root.GetInstanceID()))
                    {
                        yield return root;
                    }
                }
            }

            foreach (var candidate in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (candidate == null || candidate.transform.parent != null || !candidate.scene.IsValid() || !candidate.scene.isLoaded)
                {
                    continue;
                }

                if (seen.Add(candidate.GetInstanceID()))
                {
                    yield return candidate;
                }
            }
        }

        private static void TraverseHierarchy(GameObject gameObject, int depth, int parentInstanceId, List<NodeRecord> nodes, NodeRecord parent = null)
        {
            var record = new NodeRecord
            {
                GameObject = gameObject,
                Parent = parent,
                Dto = new HierarchyNodeDto
                {
                    instanceId = gameObject.GetInstanceID(),
                    parentInstanceId = parentInstanceId,
                    depth = depth,
                    name = gameObject.name,
                    sceneName = gameObject.scene.IsValid() ? gameObject.scene.name : "(No Scene)",
                    activeSelf = gameObject.activeSelf,
                    activeInHierarchy = gameObject.activeInHierarchy,
                    hidden = gameObject.hideFlags != HideFlags.None,
                    tag = gameObject.tag,
                    layer = gameObject.layer
                }
            };

            nodes.Add(record);
            for (var childIndex = 0; childIndex < gameObject.transform.childCount; childIndex++)
            {
                TraverseHierarchy(gameObject.transform.GetChild(childIndex).gameObject, depth + 1, gameObject.GetInstanceID(), nodes, record);
            }
        }

        private static bool MatchesQuery(GameObject gameObject, string query)
        {
            if (gameObject.name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            if (gameObject.tag.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            foreach (var component in gameObject.GetComponents<Component>())
            {
                if (component != null && component.GetType().Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static GameObject FindGameObjectRecursive(Transform current, int instanceId)
        {
            if (current.gameObject.GetInstanceID() == instanceId)
            {
                return current.gameObject;
            }

            for (var i = 0; i < current.childCount; i++)
            {
                var found = FindGameObjectRecursive(current.GetChild(i), instanceId);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private static void TraverseForPattern(GameObject gameObject, string pattern, List<GameObject> results)
        {
            if (MatchesPattern(gameObject.name, pattern) || MatchesPattern($"[{gameObject.GetInstanceID()}]", pattern))
            {
                results.Add(gameObject);
            }

            for (var i = 0; i < gameObject.transform.childCount; i++)
            {
                TraverseForPattern(gameObject.transform.GetChild(i).gameObject, pattern, results);
            }
        }

        private static bool MatchesPattern(string value, string pattern)
        {
            var normalized = string.IsNullOrWhiteSpace(pattern) ? "*" : pattern.Trim();
            if (string.Equals(normalized, "*", StringComparison.Ordinal))
            {
                return true;
            }

            var regex = "^" + Regex.Escape(normalized).Replace("\\*", ".*") + "$";
            return Regex.IsMatch(value ?? string.Empty, regex, RegexOptions.IgnoreCase);
        }

        private static InspectorComponentDto BuildComponent(Component component)
        {
            var members = new List<InspectorMemberDto>();
            AddComponentMembers(component, members);
            return new InspectorComponentDto
            {
                instanceId = component.GetInstanceID(),
                typeName = component.GetType().Name,
                enabled = component is Behaviour behaviour ? behaviour.enabled : true,
                canToggleEnabled = component is Behaviour,
                canDestroy = component is not Transform,
                members = members.ToArray()
            };
        }

        private static void AddGameObjectMembers(GameObject gameObject, List<InspectorMemberDto> members)
        {
            members.Add(CreateMember("name", "Name", typeof(string), gameObject.name, true, 0));
            members.Add(CreateMember("tag", "Tag", typeof(string), gameObject.tag, true, 0));
            members.Add(CreateMember("layer", "Layer", typeof(int), gameObject.layer, true, 0));
            members.Add(CreateMember("activeSelf", "Active", typeof(bool), gameObject.activeSelf, true, 0));
        }

        private static void AddComponentMembers(Component component, List<InspectorMemberDto> members)
        {
            var type = component.GetType();
            if (component is Behaviour behaviour)
            {
                members.Add(CreateMember("enabled", "Enabled", typeof(bool), behaviour.enabled, true, 0));
            }

            if (component is Transform transform)
            {
                members.Add(CreateMember("position", "Position", typeof(Vector3), transform.position, true, 0));
                AddExpandedSpecialLeafMembers("position", typeof(Vector3), transform.position, 0, members);
                members.Add(CreateMember("localPosition", "Local Position", typeof(Vector3), transform.localPosition, true, 0));
                AddExpandedSpecialLeafMembers("localPosition", typeof(Vector3), transform.localPosition, 0, members);
                members.Add(CreateMember("eulerAngles", "Euler Angles", typeof(Vector3), transform.eulerAngles, true, 0));
                AddExpandedSpecialLeafMembers("eulerAngles", typeof(Vector3), transform.eulerAngles, 0, members);
                members.Add(CreateMember("localEulerAngles", "Local Euler", typeof(Vector3), transform.localEulerAngles, true, 0));
                AddExpandedSpecialLeafMembers("localEulerAngles", typeof(Vector3), transform.localEulerAngles, 0, members);
                members.Add(CreateMember("localScale", "Local Scale", typeof(Vector3), transform.localScale, true, 0));
                AddExpandedSpecialLeafMembers("localScale", typeof(Vector3), transform.localScale, 0, members);
            }

            var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var field in GetSerializableFields(type))
            {
                if (!seenPaths.Add(field.Name))
                {
                    continue;
                }

                AddFieldMembers(component, field, field.Name, 0, members);
            }
        }

        private static void AddFieldMembers(object root, FieldInfo field, string path, int depth, List<InspectorMemberDto> members)
        {
            object value;
            try
            {
                value = field.GetValue(root);
            }
            catch
            {
                members.Add(new InspectorMemberDto
                {
                    path = path,
                    label = Nicify(field.Name),
                    typeName = field.FieldType.Name,
                    value = "(unavailable)",
                    editable = false,
                    multiline = false,
                    depth = depth
                });
                return;
            }

            if (ValueCodec.IsLeafType(field.FieldType))
            {
                members.Add(CreateMember(path, Nicify(field.Name), field.FieldType, value, ValueCodec.IsEditableType(field.FieldType), depth));
                AddExpandedSpecialLeafMembers(path, field.FieldType, value, depth, members);
                return;
            }

            if (ValueCodec.IsEditableCollection(field.FieldType))
            {
                members.Add(CreateMember(path, Nicify(field.Name), field.FieldType, value, true, depth));
                AddCollectionMembers(path, field.FieldType, value, depth, members);
                return;
            }

            members.Add(new InspectorMemberDto
            {
                path = path,
                label = Nicify(field.Name),
                typeName = field.FieldType.Name,
                value = ValueCodec.FormatSummary(value, field.FieldType),
                editable = false,
                multiline = false,
                depth = depth
            });

            if (value == null || depth >= MaxInspectionDepth || !IsInspectableComposite(field.FieldType))
            {
                return;
            }

            foreach (var childField in GetSerializableFields(field.FieldType))
            {
                AddFieldMembers(value, childField, $"{path}.{childField.Name}", depth + 1, members);
            }
        }

        private static void AddCollectionMembers(string path, Type collectionType, object value, int depth, List<InspectorMemberDto> members)
        {
            if (value == null || depth >= MaxInspectionDepth)
            {
                return;
            }

            if (!TryGetCollectionAdapter(value, collectionType, out var list, out var elementType, out _))
            {
                return;
            }

            var expandedCount = Mathf.Min(MaxExpandedCollectionItems, list.Count);
            for (var index = 0; index < expandedCount; index++)
            {
                var elementValue = list[index];
                var elementPath = $"{path}[{index}]";
                var elementLabel = $"Element {index}";
                AddValueMembers(elementPath, elementLabel, elementType, elementValue, depth + 1, members);
            }

            if (list.Count > expandedCount)
            {
                members.Add(new InspectorMemberDto
                {
                    path = path,
                    label = $"{list.Count - expandedCount} More Elements",
                    typeName = collectionType.Name,
                    value = "...",
                    editable = false,
                    multiline = false,
                    depth = depth + 1
                });
            }
        }

        private static void AddValueMembers(string path, string label, Type type, object value, int depth, List<InspectorMemberDto> members)
        {
            if (ValueCodec.IsLeafType(type))
            {
                members.Add(CreateMember(path, label, type, value, ValueCodec.IsEditableType(type), depth));
                AddExpandedSpecialLeafMembers(path, type, value, depth, members);
                return;
            }

            if (ValueCodec.IsEditableCollection(type))
            {
                members.Add(CreateMember(path, label, type, value, true, depth));
                AddCollectionMembers(path, type, value, depth, members);
                return;
            }

            members.Add(new InspectorMemberDto
            {
                path = path,
                label = label,
                typeName = type.Name,
                value = ValueCodec.FormatSummary(value, type),
                editable = false,
                multiline = false,
                depth = depth
            });

            if (value == null || depth >= MaxInspectionDepth || !IsInspectableComposite(type))
            {
                return;
            }

            foreach (var childField in GetSerializableFields(type))
            {
                AddFieldMembers(value, childField, $"{path}.{childField.Name}", depth + 1, members);
            }
        }

        private static void AddExpandedSpecialLeafMembers(string path, Type type, object value, int depth, List<InspectorMemberDto> members)
        {
            if (value == null || depth >= MaxInspectionDepth)
            {
                return;
            }

            type = Nullable.GetUnderlyingType(type) ?? type;
            switch (type)
            {
                case var _ when type == typeof(Vector2):
                {
                    var vector = (Vector2)value;
                    members.Add(CreateMember($"{path}.x", "X", typeof(float), vector.x, true, depth + 1));
                    members.Add(CreateMember($"{path}.y", "Y", typeof(float), vector.y, true, depth + 1));
                    break;
                }
                case var _ when type == typeof(Vector3):
                {
                    var vector = (Vector3)value;
                    members.Add(CreateMember($"{path}.x", "X", typeof(float), vector.x, true, depth + 1));
                    members.Add(CreateMember($"{path}.y", "Y", typeof(float), vector.y, true, depth + 1));
                    members.Add(CreateMember($"{path}.z", "Z", typeof(float), vector.z, true, depth + 1));
                    break;
                }
                case var _ when type == typeof(Vector4):
                {
                    var vector = (Vector4)value;
                    members.Add(CreateMember($"{path}.x", "X", typeof(float), vector.x, true, depth + 1));
                    members.Add(CreateMember($"{path}.y", "Y", typeof(float), vector.y, true, depth + 1));
                    members.Add(CreateMember($"{path}.z", "Z", typeof(float), vector.z, true, depth + 1));
                    members.Add(CreateMember($"{path}.w", "W", typeof(float), vector.w, true, depth + 1));
                    break;
                }
                case var _ when type == typeof(Quaternion):
                {
                    var euler = ((Quaternion)value).eulerAngles;
                    members.Add(CreateMember($"{path}.eulerAngles.x", "Euler X", typeof(float), euler.x, true, depth + 1));
                    members.Add(CreateMember($"{path}.eulerAngles.y", "Euler Y", typeof(float), euler.y, true, depth + 1));
                    members.Add(CreateMember($"{path}.eulerAngles.z", "Euler Z", typeof(float), euler.z, true, depth + 1));
                    break;
                }
                case var _ when type == typeof(Color):
                {
                    var color = (Color)value;
                    members.Add(CreateMember($"{path}.r", "R", typeof(float), color.r, true, depth + 1));
                    members.Add(CreateMember($"{path}.g", "G", typeof(float), color.g, true, depth + 1));
                    members.Add(CreateMember($"{path}.b", "B", typeof(float), color.b, true, depth + 1));
                    members.Add(CreateMember($"{path}.a", "A", typeof(float), color.a, true, depth + 1));
                    break;
                }
                case var _ when type == typeof(Color32):
                {
                    var color = (Color32)value;
                    members.Add(CreateMember($"{path}.r", "R", typeof(byte), color.r, true, depth + 1));
                    members.Add(CreateMember($"{path}.g", "G", typeof(byte), color.g, true, depth + 1));
                    members.Add(CreateMember($"{path}.b", "B", typeof(byte), color.b, true, depth + 1));
                    members.Add(CreateMember($"{path}.a", "A", typeof(byte), color.a, true, depth + 1));
                    break;
                }
                case var _ when type == typeof(Rect):
                {
                    var rect = (Rect)value;
                    members.Add(CreateMember($"{path}.x", "X", typeof(float), rect.x, true, depth + 1));
                    members.Add(CreateMember($"{path}.y", "Y", typeof(float), rect.y, true, depth + 1));
                    members.Add(CreateMember($"{path}.width", "Width", typeof(float), rect.width, true, depth + 1));
                    members.Add(CreateMember($"{path}.height", "Height", typeof(float), rect.height, true, depth + 1));
                    break;
                }
                case var _ when type == typeof(Bounds):
                {
                    var bounds = (Bounds)value;
                    members.Add(CreateMember($"{path}.center", "Center", typeof(Vector3), bounds.center, true, depth + 1));
                    AddExpandedSpecialLeafMembers($"{path}.center", typeof(Vector3), bounds.center, depth + 1, members);
                    members.Add(CreateMember($"{path}.size", "Size", typeof(Vector3), bounds.size, true, depth + 1));
                    AddExpandedSpecialLeafMembers($"{path}.size", typeof(Vector3), bounds.size, depth + 1, members);
                    break;
                }
            }
        }

        private static IEnumerable<FieldInfo> GetSerializableFields(Type type)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (var field in type.GetFields(flags))
            {
                if (field.IsStatic || field.IsLiteral || field.IsInitOnly)
                {
                    continue;
                }

                if (Attribute.IsDefined(field, typeof(NonSerializedAttribute)))
                {
                    continue;
                }

                if (field.Name.Contains("k__BackingField", StringComparison.Ordinal))
                {
                    continue;
                }

                if (field.IsPublic || Attribute.IsDefined(field, typeof(SerializeField)))
                {
                    yield return field;
                }
            }
        }

        private static InspectorMemberDto CreateMember(string path, string label, Type type, object value, bool editable, int depth)
        {
            return new InspectorMemberDto
            {
                path = path,
                label = label,
                typeName = type.Name,
                value = ValueCodec.Format(value, type),
                editable = editable,
                multiline = type == typeof(string) && (value as string)?.IndexOf('\n') >= 0,
                depth = depth
            };
        }

        private static bool IsInspectableComposite(Type type)
        {
            return (type.IsClass || (type.IsValueType && !type.IsPrimitive)) &&
                   type != typeof(string) &&
                   !typeof(Object).IsAssignableFrom(type) &&
                   Attribute.IsDefined(type, typeof(SerializableAttribute));
        }

        private static string Nicify(string raw)
        {
            if (string.IsNullOrEmpty(raw))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(raw.Length + 8);
            var previousWasLower = false;
            foreach (var character in raw)
            {
                if (character == '_')
                {
                    builder.Append(' ');
                    previousWasLower = false;
                    continue;
                }

                if (char.IsUpper(character) && previousWasLower)
                {
                    builder.Append(' ');
                }

                builder.Append(builder.Length == 0 ? char.ToUpperInvariant(character) : character);
                previousWasLower = char.IsLetter(character) && char.IsLower(character);
            }

            return builder.ToString();
        }

        private static bool TryGetObjectPathValue(GameObject gameObject, string memberPath, out object value, out Type valueType, out string error)
        {
            value = null;
            valueType = null;
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(memberPath))
            {
                error = "Property path is required.";
                return false;
            }

            var segments = memberPath.Split('.');
            if (!TryResolveGameObjectRoot(gameObject, segments, out var root, out var rootType, out var startIndex, out error))
            {
                return false;
            }

            return TryGetPathValueInternal(root, rootType, segments, startIndex, out value, out valueType, out error);
        }

        private static bool TrySetObjectPathValue(object rootObject, Type rootType, string memberPath, string rawValue, out string error)
        {
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(memberPath))
            {
                error = "Property path is required.";
                return false;
            }

            if (rootObject is GameObject gameObject)
            {
                var segments = memberPath.Split('.');
                if (!TryResolveGameObjectRoot(gameObject, segments, out var resolvedRoot, out var resolvedType, out var startIndex, out error))
                {
                    return false;
                }

                return TrySetPathValueInternal(resolvedRoot, resolvedType, segments, startIndex, rawValue, out _, out error);
            }

            return TrySetPathValueInternal(rootObject, rootType, memberPath.Split('.'), 0, rawValue, out _, out error);
        }

        private static bool TryResolveGameObjectRoot(GameObject gameObject, string[] segments, out object root, out Type rootType, out int startIndex, out string error)
        {
            root = gameObject;
            rootType = typeof(GameObject);
            startIndex = 0;
            error = string.Empty;

            if (segments.Length == 0)
            {
                error = "Property path is empty.";
                return false;
            }

            var first = ParsePathSegment(segments[0]).MemberName;
            if (string.Equals(first, "self", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(first, "gameObject", StringComparison.OrdinalIgnoreCase))
            {
                startIndex = 1;
                return true;
            }

            if (string.Equals(first, "transform", StringComparison.OrdinalIgnoreCase))
            {
                root = gameObject.transform;
                rootType = typeof(Transform);
                startIndex = 1;
                return true;
            }

            if (FindMember(typeof(GameObject), first, false) != null || string.Equals(first, "activeSelf", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            foreach (var component in gameObject.GetComponents<Component>())
            {
                if (component != null && IsComponentNameMatch(component.GetType(), first))
                {
                    root = component;
                    rootType = component.GetType();
                    startIndex = 1;
                    return true;
                }
            }

            error = $"'{first}' does not match a GameObject member or component name.";
            return false;
        }

        private static bool TryGetPathValueInternal(object current, Type currentType, string[] segments, int index, out object value, out Type valueType, out string error)
        {
            value = current;
            valueType = currentType;
            error = string.Empty;

            if (index >= segments.Length)
            {
                return true;
            }

            if (!TryResolveSegmentValue(current, currentType, ParsePathSegment(segments[index]), out value, out valueType, out error))
            {
                return false;
            }

            if (index == segments.Length - 1)
            {
                return true;
            }

            if (value == null)
            {
                error = $"Member '{segments[index]}' is null.";
                return false;
            }

            return TryGetPathValueInternal(value, valueType, segments, index + 1, out value, out valueType, out error);
        }

        private static bool TrySetPathValueInternal(object current, Type currentType, string[] segments, int index, string rawValue, out object updatedObject, out string error)
        {
            updatedObject = current;
            error = string.Empty;

            if (index >= segments.Length)
            {
                error = "Property path is incomplete.";
                return false;
            }

            var segment = ParsePathSegment(segments[index]);
            if (index == segments.Length - 1)
            {
                return TrySetTerminalSegmentValue(current, currentType, segment, rawValue, out updatedObject, out error);
            }

            if (!TryResolveIntermediateSegment(current, currentType, segment, out var childValue, out var childType, out var intermediateMember, out var collection, out error))
            {
                return false;
            }

            if (!TrySetPathValueInternal(childValue, childType, segments, index + 1, rawValue, out var updatedChild, out error))
            {
                return false;
            }

            if (segment.HasIndex)
            {
                if (!TrySetIndexedValue(collection, segment.Index, updatedChild, out error))
                {
                    return false;
                }
            }
            else if (childType.IsValueType && !TrySetMemberValue(current, intermediateMember, updatedChild, out error))
            {
                return false;
            }

            updatedObject = current;
            return true;
        }

        private static bool TryResolveSegmentValue(object current, Type currentType, PathSegmentDescriptor segment, out object value, out Type valueType, out string error)
        {
            value = current;
            valueType = currentType;
            error = string.Empty;

            if (!string.IsNullOrEmpty(segment.MemberName))
            {
                var member = FindMember(currentType, segment.MemberName, current == null);
                if (member == null)
                {
                    if (!TryHandleReadOnlySpecialValue(current, currentType, segment.MemberName, out value, out valueType))
                    {
                        error = $"Member '{segment.MemberName}' was not found on {currentType.Name}.";
                        return false;
                    }
                }
                else
                {
                    value = GetMemberValue(current, member);
                    valueType = GetMemberType(member);
                }
            }

            if (!segment.HasIndex)
            {
                return true;
            }

            return TryGetIndexedValue(value, valueType, segment.Index, out value, out valueType, out error);
        }

        private static bool TryResolveIntermediateSegment(object current, Type currentType, PathSegmentDescriptor segment, out object childValue, out Type childType, out MemberInfo member, out object collection, out string error)
        {
            childValue = null;
            childType = null;
            member = null;
            collection = null;
            error = string.Empty;

            if (!string.IsNullOrEmpty(segment.MemberName))
            {
                member = FindMember(currentType, segment.MemberName, current == null);
                if (member == null)
                {
                    error = $"Member '{segment.MemberName}' was not found on {currentType.Name}.";
                    return false;
                }

                childValue = GetMemberValue(current, member);
                childType = GetMemberType(member);
            }
            else
            {
                childValue = current;
                childType = currentType;
            }

            if (childValue == null)
            {
                error = $"Member '{segment.MemberName}' is null.";
                return false;
            }

            if (!segment.HasIndex)
            {
                return true;
            }

            collection = childValue;
            return TryGetIndexedValue(childValue, childType, segment.Index, out childValue, out childType, out error);
        }

        private static bool TrySetTerminalSegmentValue(object current, Type currentType, PathSegmentDescriptor segment, string rawValue, out object updatedObject, out string error)
        {
            updatedObject = current;
            error = string.Empty;

            if (segment.HasIndex)
            {
                object collection = current;
                Type collectionType = currentType;
                if (!string.IsNullOrEmpty(segment.MemberName))
                {
                    var collectionMember = FindMember(currentType, segment.MemberName, current == null);
                    if (collectionMember == null)
                    {
                        error = $"Member '{segment.MemberName}' was not found on {currentType.Name}.";
                        return false;
                    }

                    collection = GetMemberValue(current, collectionMember);
                    collectionType = GetMemberType(collectionMember);
                }

                if (!TryGetCollectionAdapter(collection, collectionType, out _, out var elementType, out error))
                {
                    return false;
                }

                if (!ValueCodec.TryConvert(rawValue, elementType, out var indexedValue))
                {
                    error = $"Could not convert '{rawValue}' to {elementType.Name}.";
                    return false;
                }

                return TrySetIndexedValue(collection, segment.Index, indexedValue, out error);
            }

            if (TryHandleSpecialWrite(current, currentType, segment.MemberName, rawValue, out error))
            {
                return true;
            }

            var member = FindMember(currentType, segment.MemberName, current == null);
            if (member == null)
            {
                error = $"Member '{segment.MemberName}' was not found on {currentType.Name}.";
                return false;
            }

            var targetType = GetMemberType(member);
            if (!ValueCodec.TryConvert(rawValue, targetType, out var converted))
            {
                error = $"Could not convert '{rawValue}' to {targetType.Name}.";
                return false;
            }

            return TrySetMemberValue(current, member, converted, out error);
        }

        private static bool TryGetIndexedValue(object collection, Type collectionType, int index, out object value, out Type valueType, out string error)
        {
            value = null;
            valueType = null;
            if (!TryGetCollectionAdapter(collection, collectionType, out var list, out var elementType, out error))
            {
                return false;
            }

            if (index < 0 || index >= list.Count)
            {
                error = $"Index {index} is out of range.";
                return false;
            }

            value = list[index];
            valueType = elementType;
            error = string.Empty;
            return true;
        }

        private static bool TrySetIndexedValue(object collection, int index, object value, out string error)
        {
            if (collection is not IList list)
            {
                error = "Indexed access requires an array or IList.";
                return false;
            }

            if (index < 0 || index >= list.Count)
            {
                error = $"Index {index} is out of range.";
                return false;
            }

            list[index] = value;
            error = string.Empty;
            return true;
        }

        private static bool TryGetCollectionAdapter(object collection, Type collectionType, out IList list, out Type elementType, out string error)
        {
            list = collection as IList;
            elementType = GetCollectionElementType(collectionType);
            if (list == null || elementType == null)
            {
                error = $"Type '{collectionType?.Name ?? "null"}' does not support indexed access.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        private static Type GetCollectionElementType(Type collectionType)
        {
            if (collectionType == null)
            {
                return null;
            }

            collectionType = Nullable.GetUnderlyingType(collectionType) ?? collectionType;
            if (collectionType.IsArray)
            {
                return collectionType.GetElementType();
            }

            if (collectionType.IsGenericType && typeof(IList).IsAssignableFrom(collectionType))
            {
                return collectionType.GetGenericArguments()[0];
            }

            return typeof(IList).IsAssignableFrom(collectionType) ? typeof(object) : null;
        }

        private static PathSegmentDescriptor ParsePathSegment(string rawSegment)
        {
            if (string.IsNullOrWhiteSpace(rawSegment))
            {
                return new PathSegmentDescriptor(string.Empty, false, -1);
            }

            var trimmed = rawSegment.Trim();
            var bracketStart = trimmed.IndexOf('[');
            if (bracketStart < 0 || !trimmed.EndsWith("]", StringComparison.Ordinal))
            {
                return new PathSegmentDescriptor(trimmed, false, -1);
            }

            var memberName = bracketStart == 0 ? string.Empty : trimmed.Substring(0, bracketStart);
            var indexText = trimmed.Substring(bracketStart + 1, trimmed.Length - bracketStart - 2);
            return int.TryParse(indexText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index)
                ? new PathSegmentDescriptor(memberName, true, index)
                : new PathSegmentDescriptor(trimmed, false, -1);
        }

        private static bool TryHandleReadOnlySpecialValue(object target, Type targetType, string memberName, out object value, out Type valueType)
        {
            value = null;
            valueType = null;
            if (targetType == typeof(GameObject) && string.Equals(memberName, "activeSelf", StringComparison.OrdinalIgnoreCase))
            {
                value = ((GameObject)target).activeSelf;
                valueType = typeof(bool);
                return true;
            }

            return false;
        }

        private static bool TryHandleSpecialWrite(object target, Type targetType, string memberName, string rawValue, out string error)
        {
            error = string.Empty;
            if (targetType == typeof(GameObject) && string.Equals(memberName, "activeSelf", StringComparison.OrdinalIgnoreCase))
            {
                if (!ValueCodec.TryConvert(rawValue, typeof(bool), out var converted))
                {
                    error = $"Could not convert '{rawValue}' to bool.";
                    return false;
                }

                ((GameObject)target).SetActive((bool)converted);
                return true;
            }

            return false;
        }

        private static bool TrySetMemberValue(object target, MemberInfo member, object value, out string error)
        {
            error = string.Empty;
            switch (member)
            {
                case FieldInfo field:
                    field.SetValue(target, value);
                    return true;
                case PropertyInfo property:
                    if (!property.CanWrite)
                    {
                        error = $"Property '{property.Name}' is read-only.";
                        return false;
                    }

                    property.SetValue(target, value, null);
                    return true;
                default:
                    error = $"Unsupported member '{member.Name}'.";
                    return false;
            }
        }

        private static object GetMemberValue(object target, MemberInfo member)
        {
            return member switch
            {
                FieldInfo field => field.GetValue(target),
                PropertyInfo property => property.GetValue(target, null),
                _ => null
            };
        }

        private static Type GetMemberType(MemberInfo member)
        {
            return member switch
            {
                FieldInfo field => field.FieldType,
                PropertyInfo property => property.PropertyType,
                _ => typeof(object)
            };
        }

        private static MemberInfo FindMember(Type type, string memberName, bool isStatic)
        {
            var flags = BindingFlags.Public | BindingFlags.NonPublic | (isStatic ? BindingFlags.Static : BindingFlags.Instance);
            foreach (var property in type.GetProperties(flags))
            {
                if (string.Equals(property.Name, memberName, StringComparison.OrdinalIgnoreCase))
                {
                    return property;
                }
            }

            foreach (var field in type.GetFields(flags))
            {
                if (string.Equals(field.Name, memberName, StringComparison.OrdinalIgnoreCase))
                {
                    return field;
                }
            }

            return null;
        }

        private static bool IsComponentNameMatch(Type componentType, string candidate)
        {
            return string.Equals(componentType.Name, candidate, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(componentType.FullName, candidate, StringComparison.OrdinalIgnoreCase);
        }

        private static Type FindComponentType(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException exception)
                {
                    types = exception.Types.Where(type => type != null).ToArray();
                }

                foreach (var type in types)
                {
                    if (type == null || type.IsAbstract || !typeof(Component).IsAssignableFrom(type))
                    {
                        continue;
                    }

                    if (string.Equals(type.Name, name, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(type.FullName, name, StringComparison.OrdinalIgnoreCase))
                    {
                        return type;
                    }
                }
            }

            return null;
        }

        private readonly struct PathSegmentDescriptor
        {
            public PathSegmentDescriptor(string memberName, bool hasIndex, int index)
            {
                MemberName = memberName ?? string.Empty;
                HasIndex = hasIndex;
                Index = index;
            }

            public string MemberName { get; }

            public bool HasIndex { get; }

            public int Index { get; }
        }

        private sealed class NodeRecord
        {
            public GameObject GameObject;
            public HierarchyNodeDto Dto;
            public NodeRecord Parent;
        }

        private static class ValueCodec
        {
            public static bool IsLeafType(Type type)
            {
                type = Nullable.GetUnderlyingType(type) ?? type;
                return type.IsPrimitive ||
                       type.IsEnum ||
                       type == typeof(string) ||
                       type == typeof(decimal) ||
                       type == typeof(Vector2) ||
                       type == typeof(Vector3) ||
                       type == typeof(Vector4) ||
                       type == typeof(Quaternion) ||
                       type == typeof(Color) ||
                       type == typeof(Color32) ||
                       type == typeof(Rect) ||
                       type == typeof(Bounds) ||
                       type == typeof(LayerMask);
            }

            public static bool IsEditableType(Type type)
            {
                return IsLeafType(type);
            }

            public static bool IsEditableCollection(Type type)
            {
                var elementType = GetElementType(type);
                return elementType != null && IsLeafType(elementType);
            }

            public static string FormatSummary(object value, Type type)
            {
                if (value == null)
                {
                    return "null";
                }

                if (value is IList list)
                {
                    return $"{type.Name} ({list.Count})";
                }

                return type.Name;
            }

            public static string Format(object value, Type type)
            {
                if (value == null)
                {
                    return "null";
                }

                type = Nullable.GetUnderlyingType(type) ?? type;
                if (type == typeof(string))
                {
                    return (string)value;
                }

                if (type == typeof(bool))
                {
                    return ((bool)value) ? "true" : "false";
                }

                if (type.IsEnum)
                {
                    return value.ToString();
                }

                if (type == typeof(float))
                {
                    return ((float)value).ToString("0.###", CultureInfo.InvariantCulture);
                }

                if (type == typeof(double))
                {
                    return ((double)value).ToString("0.###", CultureInfo.InvariantCulture);
                }

                if (type == typeof(Vector2))
                {
                    var vector = (Vector2)value;
                    return $"[{vector.x:0.###}, {vector.y:0.###}]";
                }

                if (type == typeof(Vector3))
                {
                    var vector = (Vector3)value;
                    return $"[{vector.x:0.###}, {vector.y:0.###}, {vector.z:0.###}]";
                }

                if (type == typeof(Vector4))
                {
                    var vector = (Vector4)value;
                    return $"[{vector.x:0.###}, {vector.y:0.###}, {vector.z:0.###}, {vector.w:0.###}]";
                }

                if (type == typeof(Quaternion))
                {
                    var euler = ((Quaternion)value).eulerAngles;
                    return $"[{euler.x:0.###}, {euler.y:0.###}, {euler.z:0.###}]";
                }

                if (type == typeof(Color))
                {
                    return "#" + ColorUtility.ToHtmlStringRGBA((Color)value);
                }

                if (type == typeof(Color32))
                {
                    return "#" + ColorUtility.ToHtmlStringRGBA((Color32)value);
                }

                if (type == typeof(Rect))
                {
                    var rect = (Rect)value;
                    return $"[{rect.x:0.###}, {rect.y:0.###}, {rect.width:0.###}, {rect.height:0.###}]";
                }

                if (type == typeof(Bounds))
                {
                    var bounds = (Bounds)value;
                    return $"[{bounds.center.x:0.###}, {bounds.center.y:0.###}, {bounds.center.z:0.###}, {bounds.size.x:0.###}, {bounds.size.y:0.###}, {bounds.size.z:0.###}]";
                }

                if (type == typeof(LayerMask))
                {
                    return ((LayerMask)value).value.ToString(CultureInfo.InvariantCulture);
                }

                if (value is IList list)
                {
                    var builder = new StringBuilder();
                    builder.Append('[');
                    for (var index = 0; index < list.Count; index++)
                    {
                        if (index > 0)
                        {
                            builder.Append(", ");
                        }

                        var item = list[index];
                        builder.Append(item == null ? "null" : Format(item, item.GetType()));
                    }

                    builder.Append(']');
                    return builder.ToString();
                }

                if (value is Object unityObject)
                {
                    return $"{unityObject.name} [{unityObject.GetInstanceID()}]";
                }

                if (value is IFormattable formattable)
                {
                    return formattable.ToString(null, CultureInfo.InvariantCulture);
                }

                return value.ToString();
            }

            public static bool TryConvert(string rawValue, Type targetType, out object value)
            {
                value = null;
                targetType = Nullable.GetUnderlyingType(targetType) ?? targetType;

                if (targetType == typeof(string))
                {
                    value = rawValue ?? string.Empty;
                    return true;
                }

                if (string.IsNullOrWhiteSpace(rawValue))
                {
                    return false;
                }

                if (targetType == typeof(bool) && bool.TryParse(rawValue, out var boolValue))
                {
                    value = boolValue;
                    return true;
                }

                if (targetType == typeof(int) && int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intValue))
                {
                    value = intValue;
                    return true;
                }

                if (targetType == typeof(float) && float.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var floatValue))
                {
                    value = floatValue;
                    return true;
                }

                if (targetType == typeof(double) && double.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var doubleValue))
                {
                    value = doubleValue;
                    return true;
                }

                if (targetType == typeof(long) && long.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var longValue))
                {
                    value = longValue;
                    return true;
                }

                if (targetType == typeof(short) && short.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var shortValue))
                {
                    value = shortValue;
                    return true;
                }

                if (targetType == typeof(byte) && byte.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var byteValue))
                {
                    value = byteValue;
                    return true;
                }

                if (targetType == typeof(decimal) && decimal.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var decimalValue))
                {
                    value = decimalValue;
                    return true;
                }

                if (targetType.IsEnum)
                {
                    try
                    {
                        value = Enum.Parse(targetType, rawValue, true);
                        return true;
                    }
                    catch
                    {
                        return false;
                    }
                }

                if (targetType == typeof(Vector2))
                {
                    var values = ParseFloatArray(rawValue, 2);
                    if (values == null)
                    {
                        return false;
                    }

                    value = new Vector2(values[0], values[1]);
                    return true;
                }

                if (targetType == typeof(Vector3))
                {
                    var values = ParseFloatArray(rawValue, 3);
                    if (values == null)
                    {
                        return false;
                    }

                    value = new Vector3(values[0], values[1], values[2]);
                    return true;
                }

                if (targetType == typeof(Vector4))
                {
                    var values = ParseFloatArray(rawValue, 4);
                    if (values == null)
                    {
                        return false;
                    }

                    value = new Vector4(values[0], values[1], values[2], values[3]);
                    return true;
                }

                if (targetType == typeof(Quaternion))
                {
                    var values = ParseFloatArray(rawValue, 3);
                    if (values == null)
                    {
                        return false;
                    }

                    value = Quaternion.Euler(values[0], values[1], values[2]);
                    return true;
                }

                if (targetType == typeof(Color))
                {
                    if (ColorUtility.TryParseHtmlString(rawValue, out var color))
                    {
                        value = color;
                        return true;
                    }

                    var values = ParseFloatArray(rawValue, 3, 4);
                    if (values == null)
                    {
                        return false;
                    }

                    value = values.Length == 4
                        ? new Color(values[0], values[1], values[2], values[3])
                        : new Color(values[0], values[1], values[2], 1f);
                    return true;
                }

                if (targetType == typeof(Color32))
                {
                    if (ColorUtility.TryParseHtmlString(rawValue, out var color))
                    {
                        value = (Color32)color;
                        return true;
                    }

                    return false;
                }

                if (targetType == typeof(Rect))
                {
                    var values = ParseFloatArray(rawValue, 4);
                    if (values == null)
                    {
                        return false;
                    }

                    value = new Rect(values[0], values[1], values[2], values[3]);
                    return true;
                }

                if (targetType == typeof(Bounds))
                {
                    var values = ParseFloatArray(rawValue, 6);
                    if (values == null)
                    {
                        return false;
                    }

                    value = new Bounds(new Vector3(values[0], values[1], values[2]), new Vector3(values[3], values[4], values[5]));
                    return true;
                }

                if (targetType == typeof(LayerMask) && int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var layerValue))
                {
                    value = (LayerMask)layerValue;
                    return true;
                }

                var elementType = GetElementType(targetType);
                if (elementType != null)
                {
                    var tokens = SplitCollection(rawValue);
                    var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(elementType));
                    foreach (var token in tokens)
                    {
                        if (!TryConvert(token, elementType, out var item))
                        {
                            return false;
                        }

                        list.Add(item);
                    }

                    if (targetType.IsArray)
                    {
                        var array = Array.CreateInstance(elementType, list.Count);
                        list.CopyTo(array, 0);
                        value = array;
                        return true;
                    }

                    value = list;
                    return true;
                }

                return false;
            }

            private static Type GetElementType(Type type)
            {
                if (type.IsArray)
                {
                    return type.GetElementType();
                }

                if (type.IsGenericType && typeof(IList).IsAssignableFrom(type))
                {
                    return type.GetGenericArguments()[0];
                }

                return null;
            }

            private static float[] ParseFloatArray(string rawValue, params int[] allowedSizes)
            {
                var parts = SplitCollection(rawValue);
                if (!allowedSizes.Contains(parts.Count))
                {
                    return null;
                }

                var values = new float[parts.Count];
                for (var index = 0; index < parts.Count; index++)
                {
                    if (!float.TryParse(parts[index], NumberStyles.Float, CultureInfo.InvariantCulture, out values[index]))
                    {
                        return null;
                    }
                }

                return values;
            }

            private static List<string> SplitCollection(string rawValue)
            {
                var trimmed = rawValue.Trim();
                if (trimmed.StartsWith("[", StringComparison.Ordinal) && trimmed.EndsWith("]", StringComparison.Ordinal))
                {
                    trimmed = trimmed.Substring(1, trimmed.Length - 2);
                }

                var results = new List<string>();
                if (string.IsNullOrWhiteSpace(trimmed))
                {
                    return results;
                }

                foreach (var segment in trimmed.Split(','))
                {
                    results.Add(segment.Trim());
                }

                return results;
            }
        }
    }
}

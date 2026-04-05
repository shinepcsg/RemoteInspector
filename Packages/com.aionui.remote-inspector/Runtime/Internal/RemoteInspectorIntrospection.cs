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

        private static readonly ComponentPropertyDescriptor[] RectTransformPropertyDescriptors =
        {
            new("anchorMin", "Anchor Min"),
            new("anchorMax", "Anchor Max"),
            new("anchoredPosition", "Anchored Position"),
            new("sizeDelta", "Size Delta"),
            new("pivot", "Pivot"),
            new("offsetMin", "Offset Min"),
            new("offsetMax", "Offset Max")
        };

        private static readonly ComponentPropertyDescriptor[] CameraPropertyDescriptors =
        {
            new("clearFlags", "Clear Flags"),
            new("backgroundColor", "Background"),
            new("cullingMask", "Culling Mask"),
            new("orthographic", "Orthographic"),
            new("orthographicSize", "Orthographic Size"),
            new("fieldOfView", "Field Of View"),
            new("nearClipPlane", "Near Clip Plane"),
            new("farClipPlane", "Far Clip Plane"),
            new("depth", "Depth"),
            new("renderingPath", "Rendering Path"),
            new("allowHDR", "Allow HDR"),
            new("allowMSAA", "Allow MSAA"),
            new("useOcclusionCulling", "Use Occlusion Culling"),
            new("targetDisplay", "Target Display")
        };

        private static readonly ComponentPropertyDescriptor[] LightPropertyDescriptors =
        {
            new("type", "Type"),
            new("shape", "Shape"),
            new("range", "Range"),
            new("spotAngle", "Spot Angle"),
            new("innerSpotAngle", "Inner Spot Angle"),
            new("color", "Color"),
            new("intensity", "Intensity"),
            new("bounceIntensity", "Bounce Intensity"),
            new("shadows", "Shadows"),
            new("renderMode", "Render Mode"),
            new("cullingMask", "Culling Mask"),
            new("cookieSize", "Cookie Size"),
            new("useColorTemperature", "Use Color Temperature"),
            new("colorTemperature", "Color Temperature"),
            new("cookie", "Cookie", false),
            new("flare", "Flare", false)
        };

        private static readonly ComponentPropertyDescriptor[] AudioSourcePropertyDescriptors =
        {
            new("clip", "Clip", false),
            new("outputAudioMixerGroup", "Output", false),
            new("playOnAwake", "Play On Awake"),
            new("loop", "Loop"),
            new("mute", "Mute"),
            new("priority", "Priority"),
            new("volume", "Volume"),
            new("pitch", "Pitch"),
            new("panStereo", "Stereo Pan"),
            new("spatialBlend", "Spatial Blend"),
            new("reverbZoneMix", "Reverb Zone Mix"),
            new("dopplerLevel", "Doppler Level"),
            new("spread", "Spread"),
            new("minDistance", "Min Distance"),
            new("maxDistance", "Max Distance")
        };

        private static readonly ComponentPropertyDescriptor[] RigidbodyPropertyDescriptors =
        {
            new("mass", "Mass"),
            new("linearDamping", "Linear Damping"),
            new("angularDamping", "Angular Damping"),
            new("useGravity", "Use Gravity"),
            new("isKinematic", "Is Kinematic"),
            new("interpolation", "Interpolation"),
            new("collisionDetectionMode", "Collision Detection"),
            new("constraints", "Constraints"),
            new("centerOfMass", "Center Of Mass"),
            new("linearVelocity", "Linear Velocity"),
            new("angularVelocity", "Angular Velocity"),
            new("detectCollisions", "Detect Collisions")
        };

        private static readonly ComponentPropertyDescriptor[] Rigidbody2DPropertyDescriptors =
        {
            new("bodyType", "Body Type"),
            new("mass", "Mass"),
            new("gravityScale", "Gravity Scale"),
            new("linearDamping", "Linear Damping"),
            new("angularDamping", "Angular Damping"),
            new("constraints", "Constraints"),
            new("interpolation", "Interpolation"),
            new("collisionDetectionMode", "Collision Detection"),
            new("simulated", "Simulated"),
            new("linearVelocity", "Linear Velocity"),
            new("angularVelocity", "Angular Velocity")
        };

        private static readonly ComponentPropertyDescriptor[] RendererPropertyDescriptors =
        {
            new("sharedMaterial", "Material", false),
            new("shadowCastingMode", "Cast Shadows"),
            new("receiveShadows", "Receive Shadows"),
            new("lightProbeUsage", "Light Probes"),
            new("reflectionProbeUsage", "Reflection Probes"),
            new("sortingLayerID", "Sorting Layer"),
            new("sortingOrder", "Order In Layer"),
            new("allowOcclusionWhenDynamic", "Dynamic Occlusion")
        };

        private static readonly ComponentPropertyDescriptor[] SpriteRendererPropertyDescriptors =
        {
            new("sprite", "Sprite", false),
            new("color", "Color"),
            new("flipX", "Flip X"),
            new("flipY", "Flip Y"),
            new("drawMode", "Draw Mode"),
            new("size", "Size"),
            new("maskInteraction", "Mask Interaction"),
            new("tileMode", "Tile Mode"),
            new("adaptiveModeThreshold", "Adaptive Mode Threshold"),
            new("sortPoint", "Sort Point")
        };

        private static readonly ComponentPropertyDescriptor[] ColliderPropertyDescriptors =
        {
            new("isTrigger", "Is Trigger"),
            new("contactOffset", "Contact Offset"),
            new("sharedMaterial", "Material", false)
        };

        private static readonly ComponentPropertyDescriptor[] BoxColliderPropertyDescriptors =
        {
            new("center", "Center"),
            new("size", "Size")
        };

        private static readonly ComponentPropertyDescriptor[] SphereColliderPropertyDescriptors =
        {
            new("center", "Center"),
            new("radius", "Radius")
        };

        private static readonly ComponentPropertyDescriptor[] CapsuleColliderPropertyDescriptors =
        {
            new("center", "Center"),
            new("radius", "Radius"),
            new("height", "Height"),
            new("direction", "Direction")
        };

        private static readonly ComponentPropertyDescriptor[] MeshColliderPropertyDescriptors =
        {
            new("sharedMesh", "Mesh", false),
            new("convex", "Convex"),
            new("cookingOptions", "Cooking Options")
        };

        private static readonly ComponentPropertyDescriptor[] Collider2DPropertyDescriptors =
        {
            new("offset", "Offset"),
            new("isTrigger", "Is Trigger"),
            new("density", "Density"),
            new("usedByEffector", "Used By Effector"),
            new("sharedMaterial", "Material", false)
        };

        private static readonly ComponentPropertyDescriptor[] BoxCollider2DPropertyDescriptors =
        {
            new("size", "Size"),
            new("edgeRadius", "Edge Radius"),
            new("autoTiling", "Auto Tiling")
        };

        private static readonly ComponentPropertyDescriptor[] CircleCollider2DPropertyDescriptors =
        {
            new("radius", "Radius")
        };

        private static readonly ComponentPropertyDescriptor[] CapsuleCollider2DPropertyDescriptors =
        {
            new("size", "Size"),
            new("direction", "Direction")
        };

        private static readonly ComponentPropertyDescriptor[] AnimatorPropertyDescriptors =
        {
            new("applyRootMotion", "Apply Root Motion"),
            new("updateMode", "Update Mode"),
            new("cullingMode", "Culling Mode"),
            new("speed", "Speed"),
            new("fireEvents", "Fire Events"),
            new("logWarnings", "Log Warnings"),
            new("avatar", "Avatar", false),
            new("runtimeAnimatorController", "Controller", false)
        };

        private static readonly ComponentPropertyDescriptor[] CharacterControllerPropertyDescriptors =
        {
            new("center", "Center"),
            new("radius", "Radius"),
            new("height", "Height"),
            new("slopeLimit", "Slope Limit"),
            new("stepOffset", "Step Offset"),
            new("skinWidth", "Skin Width"),
            new("minMoveDistance", "Min Move Distance"),
            new("detectCollisions", "Detect Collisions"),
            new("enableOverlapRecovery", "Enable Overlap Recovery")
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

                try
                {
                    components.Add(BuildComponent(component));
                }
                catch (Exception ex)
                {
                    components.Add(new InspectorComponentDto
                    {
                        instanceId = component.GetInstanceID(),
                        typeName = component.GetType().Name,
                        enabled = true,
                        canToggleEnabled = false,
                        canDestroy = component is not Transform,
                        members = new[]
                        {
                            new InspectorMemberDto
                            {
                                path = "__error",
                                label = "Error",
                                typeName = "String",
                                value = ex.Message,
                                editable = false,
                                multiline = false,
                                depth = 0,
                                controlHint = "summary",
                                options = Array.Empty<string>(),
                                referenceTypeName = string.Empty,
                                referenceInstanceId = 0
                            }
                        }
                    });
                }
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
            try
            {
                AddComponentMembers(component, members);
            }
            catch (Exception ex)
            {
                members.Add(new InspectorMemberDto
                {
                    path = "__error",
                    label = "Error",
                    typeName = "String",
                    value = ex.Message,
                    editable = false,
                    multiline = false,
                    depth = 0,
                    controlHint = "summary",
                    options = Array.Empty<string>(),
                    referenceTypeName = string.Empty,
                    referenceInstanceId = 0
                });
            }

            var canToggleEnabled = TryGetComponentEnabledState(component, out var enabledState);
            return new InspectorComponentDto
            {
                instanceId = component.GetInstanceID(),
                typeName = component.GetType().Name,
                enabled = enabledState,
                canToggleEnabled = canToggleEnabled,
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
            if (TryGetComponentEnabledState(component, out var enabledState))
            {
                members.Add(CreateMember("enabled", "Enabled", typeof(bool), enabledState, true, 0));
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

            var seenPaths = new HashSet<string>(members.Select(member => member.path), StringComparer.OrdinalIgnoreCase);
            AddBuiltInComponentMembers(component, seenPaths, members);

            foreach (var field in GetSerializableFields(type))
            {
                if (!seenPaths.Add(field.Name))
                {
                    continue;
                }

                AddFieldMembers(component, field, field.Name, 0, members);
            }
        }

        private static bool TryGetComponentEnabledState(Component component, out bool enabled)
        {
            enabled = true;
            var member = FindMember(component.GetType(), "enabled", false);
            if (member == null || !CanReadMember(member) || GetMemberType(member) != typeof(bool))
            {
                return false;
            }

            try
            {
                enabled = (bool)GetMemberValue(component, member);
                return IsMemberWritable(member);
            }
            catch
            {
                enabled = true;
                return false;
            }
        }

        private static void AddBuiltInComponentMembers(Component component, HashSet<string> seenPaths, List<InspectorMemberDto> members)
        {
            if (component is RectTransform rectTransform)
            {
                AddNamedMembers(rectTransform, RectTransformPropertyDescriptors, seenPaths, members);
            }

            if (component is Camera camera)
            {
                AddNamedMembers(camera, CameraPropertyDescriptors, seenPaths, members);
            }

            if (component is Light light)
            {
                AddNamedMembers(light, LightPropertyDescriptors, seenPaths, members);
            }

            if (component is AudioSource audioSource)
            {
                AddNamedMembers(audioSource, AudioSourcePropertyDescriptors, seenPaths, members);
            }

            if (component is Rigidbody rigidbody)
            {
                AddNamedMembers(rigidbody, RigidbodyPropertyDescriptors, seenPaths, members);
            }

            if (component is Rigidbody2D rigidbody2D)
            {
                AddNamedMembers(rigidbody2D, Rigidbody2DPropertyDescriptors, seenPaths, members);
            }

            if (component is Renderer renderer)
            {
                AddNamedMembers(renderer, RendererPropertyDescriptors, seenPaths, members);
            }

            if (component is SpriteRenderer spriteRenderer)
            {
                AddNamedMembers(spriteRenderer, SpriteRendererPropertyDescriptors, seenPaths, members);
            }

            if (component is Collider collider)
            {
                AddNamedMembers(collider, ColliderPropertyDescriptors, seenPaths, members);
            }

            if (component is BoxCollider boxCollider)
            {
                AddNamedMembers(boxCollider, BoxColliderPropertyDescriptors, seenPaths, members);
            }

            if (component is SphereCollider sphereCollider)
            {
                AddNamedMembers(sphereCollider, SphereColliderPropertyDescriptors, seenPaths, members);
            }

            if (component is CapsuleCollider capsuleCollider)
            {
                AddNamedMembers(capsuleCollider, CapsuleColliderPropertyDescriptors, seenPaths, members);
            }

            if (component is MeshCollider meshCollider)
            {
                AddNamedMembers(meshCollider, MeshColliderPropertyDescriptors, seenPaths, members);
            }

            if (component is Collider2D collider2D)
            {
                AddNamedMembers(collider2D, Collider2DPropertyDescriptors, seenPaths, members);
            }

            if (component is BoxCollider2D boxCollider2D)
            {
                AddNamedMembers(boxCollider2D, BoxCollider2DPropertyDescriptors, seenPaths, members);
            }

            if (component is CircleCollider2D circleCollider2D)
            {
                AddNamedMembers(circleCollider2D, CircleCollider2DPropertyDescriptors, seenPaths, members);
            }

            if (component is CapsuleCollider2D capsuleCollider2D)
            {
                AddNamedMembers(capsuleCollider2D, CapsuleCollider2DPropertyDescriptors, seenPaths, members);
            }

            if (component is Animator animator)
            {
                AddNamedMembers(animator, AnimatorPropertyDescriptors, seenPaths, members);
            }

            if (component is CharacterController characterController)
            {
                AddNamedMembers(characterController, CharacterControllerPropertyDescriptors, seenPaths, members);
            }
        }

        private static void AddNamedMembers(object target, IReadOnlyList<ComponentPropertyDescriptor> descriptors, HashSet<string> seenPaths, List<InspectorMemberDto> members)
        {
            var targetType = target.GetType();
            for (var index = 0; index < descriptors.Count; index++)
            {
                TryAddNamedMember(target, targetType, descriptors[index], seenPaths, members);
            }
        }

        private static void TryAddNamedMember(object target, Type targetType, ComponentPropertyDescriptor descriptor, HashSet<string> seenPaths, List<InspectorMemberDto> members)
        {
            if (!seenPaths.Add(descriptor.Path))
            {
                return;
            }

            var member = FindMember(targetType, descriptor.Path, false);
            if (member == null || !CanReadMember(member))
            {
                seenPaths.Remove(descriptor.Path);
                return;
            }

            object value;
            try
            {
                value = GetMemberValue(target, member);
            }
            catch
            {
                seenPaths.Remove(descriptor.Path);
                return;
            }

            var memberType = GetMemberType(member);
            if (ValueCodec.IsLeafType(memberType))
            {
                var editable = descriptor.Editable && IsMemberWritable(member) && ValueCodec.IsEditableType(memberType);
                members.Add(CreateMember(descriptor.Path, descriptor.Label, memberType, value, editable, 0));
                AddExpandedSpecialLeafMembers(descriptor.Path, memberType, value, 0, members);
                return;
            }

            if (ValueCodec.IsEditableCollection(memberType))
            {
                var editable = descriptor.Editable && IsMemberWritable(member);
                members.Add(CreateMember(descriptor.Path, descriptor.Label, memberType, value, editable, 0));
                AddCollectionMembers(descriptor.Path, memberType, value, 0, members);
                return;
            }

            members.Add(CreateSummaryMember(descriptor.Path, descriptor.Label, memberType, value, 0));
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
                    depth = depth,
                    controlHint = "summary",
                    options = Array.Empty<string>(),
                    referenceTypeName = string.Empty,
                    referenceInstanceId = 0
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

            members.Add(CreateSummaryMember(path, Nicify(field.Name), field.FieldType, value, depth));

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
                    depth = depth + 1,
                    controlHint = "summary",
                    options = Array.Empty<string>(),
                    referenceTypeName = string.Empty,
                    referenceInstanceId = 0
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

            members.Add(CreateSummaryMember(path, label, type, value, depth));

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
                case var _ when type == typeof(Vector2Int):
                {
                    var vector = (Vector2Int)value;
                    members.Add(CreateMember($"{path}.x", "X", typeof(int), vector.x, true, depth + 1));
                    members.Add(CreateMember($"{path}.y", "Y", typeof(int), vector.y, true, depth + 1));
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
                case var _ when type == typeof(Vector3Int):
                {
                    var vector = (Vector3Int)value;
                    members.Add(CreateMember($"{path}.x", "X", typeof(int), vector.x, true, depth + 1));
                    members.Add(CreateMember($"{path}.y", "Y", typeof(int), vector.y, true, depth + 1));
                    members.Add(CreateMember($"{path}.z", "Z", typeof(int), vector.z, true, depth + 1));
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
                case var _ when type == typeof(RectInt):
                {
                    var rect = (RectInt)value;
                    members.Add(CreateMember($"{path}.x", "X", typeof(int), rect.x, true, depth + 1));
                    members.Add(CreateMember($"{path}.y", "Y", typeof(int), rect.y, true, depth + 1));
                    members.Add(CreateMember($"{path}.width", "Width", typeof(int), rect.width, true, depth + 1));
                    members.Add(CreateMember($"{path}.height", "Height", typeof(int), rect.height, true, depth + 1));
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
                case var _ when type == typeof(BoundsInt):
                {
                    var bounds = (BoundsInt)value;
                    members.Add(CreateMember($"{path}.position", "Position", typeof(Vector3Int), bounds.position, true, depth + 1));
                    AddExpandedSpecialLeafMembers($"{path}.position", typeof(Vector3Int), bounds.position, depth + 1, members);
                    members.Add(CreateMember($"{path}.size", "Size", typeof(Vector3Int), bounds.size, true, depth + 1));
                    AddExpandedSpecialLeafMembers($"{path}.size", typeof(Vector3Int), bounds.size, depth + 1, members);
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
            type = Nullable.GetUnderlyingType(type) ?? type;
            return new InspectorMemberDto
            {
                path = path,
                label = label,
                typeName = type.Name,
                value = ValueCodec.Format(value, type),
                editable = editable && ValueCodec.IsEditableType(type),
                multiline = type == typeof(string) && (value as string)?.IndexOf('\n') >= 0,
                depth = depth,
                controlHint = ValueCodec.GetControlHint(type),
                options = ValueCodec.GetOptions(type),
                referenceTypeName = ValueCodec.IsObjectReferenceType(type) ? type.Name : string.Empty,
                referenceInstanceId = value is Object unityObject ? unityObject.GetInstanceID() : 0
            };
        }

        private static InspectorMemberDto CreateSummaryMember(string path, string label, Type type, object value, int depth)
        {
            type = Nullable.GetUnderlyingType(type) ?? type;
            return new InspectorMemberDto
            {
                path = path,
                label = label,
                typeName = type.Name,
                value = ValueCodec.FormatSummary(value, type),
                editable = false,
                multiline = false,
                depth = depth,
                controlHint = "summary",
                options = Array.Empty<string>(),
                referenceTypeName = ValueCodec.IsObjectReferenceType(type) ? type.Name : string.Empty,
                referenceInstanceId = value is Object unityObject ? unityObject.GetInstanceID() : 0
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
                    if (!IsMemberWritable(property))
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

        private static bool CanReadMember(MemberInfo member)
        {
            return member switch
            {
                FieldInfo => true,
                PropertyInfo property => property.CanRead && property.GetIndexParameters().Length == 0,
                _ => false
            };
        }

        private static bool IsMemberWritable(MemberInfo member)
        {
            return member switch
            {
                FieldInfo field => !field.IsInitOnly && !field.IsLiteral,
                PropertyInfo property => property.CanWrite && property.GetIndexParameters().Length == 0,
                _ => false
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
                if (property.GetIndexParameters().Length == 0 &&
                    string.Equals(property.Name, memberName, StringComparison.OrdinalIgnoreCase))
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

        private readonly struct ComponentPropertyDescriptor
        {
            public ComponentPropertyDescriptor(string path, string label, bool editable = true)
            {
                Path = path ?? string.Empty;
                Label = label ?? string.Empty;
                Editable = editable;
            }

            public string Path { get; }

            public string Label { get; }

            public bool Editable { get; }
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
                       type == typeof(Vector2Int) ||
                       type == typeof(Vector3) ||
                       type == typeof(Vector3Int) ||
                       type == typeof(Vector4) ||
                       type == typeof(Quaternion) ||
                       type == typeof(Color) ||
                       type == typeof(Color32) ||
                       type == typeof(Rect) ||
                       type == typeof(RectInt) ||
                       type == typeof(Bounds) ||
                       type == typeof(BoundsInt) ||
                       type == typeof(LayerMask) ||
                       IsObjectReferenceType(type);
            }

            public static bool IsEditableType(Type type)
            {
                type = Nullable.GetUnderlyingType(type) ?? type;
                return IsLeafType(type) && !IsObjectReferenceType(type);
            }

            public static bool IsObjectReferenceType(Type type)
            {
                type = Nullable.GetUnderlyingType(type) ?? type;
                return typeof(Object).IsAssignableFrom(type);
            }

            public static string GetControlHint(Type type)
            {
                type = Nullable.GetUnderlyingType(type) ?? type;
                if (type == typeof(string))
                {
                    return "text";
                }

                if (type == typeof(bool))
                {
                    return "bool";
                }

                if (type.IsEnum)
                {
                    return "enum";
                }

                if (IsObjectReferenceType(type))
                {
                    return "object";
                }

                if (type == typeof(float) || type == typeof(double) || type == typeof(decimal))
                {
                    return "number";
                }

                if (type == typeof(int) || type == typeof(uint) || type == typeof(long) || type == typeof(ulong) ||
                    type == typeof(short) || type == typeof(ushort) || type == typeof(byte) || type == typeof(sbyte))
                {
                    return "integer";
                }

                if (type == typeof(Vector2))
                {
                    return "vector2";
                }

                if (type == typeof(Vector2Int))
                {
                    return "vector2int";
                }

                if (type == typeof(Vector3))
                {
                    return "vector3";
                }

                if (type == typeof(Vector3Int))
                {
                    return "vector3int";
                }

                if (type == typeof(Vector4))
                {
                    return "vector4";
                }

                if (type == typeof(Quaternion))
                {
                    return "quaternion";
                }

                if (type == typeof(Color) || type == typeof(Color32))
                {
                    return "color";
                }

                if (type == typeof(Rect))
                {
                    return "rect";
                }

                if (type == typeof(RectInt))
                {
                    return "rectint";
                }

                if (type == typeof(Bounds))
                {
                    return "bounds";
                }

                if (type == typeof(BoundsInt))
                {
                    return "boundsint";
                }

                if (type == typeof(LayerMask))
                {
                    return "layermask";
                }

                return "text";
            }

            public static string[] GetOptions(Type type)
            {
                type = Nullable.GetUnderlyingType(type) ?? type;
                return type.IsEnum ? Enum.GetNames(type) : Array.Empty<string>();
            }

            public static bool IsEditableCollection(Type type)
            {
                var elementType = GetElementType(type);
                return elementType != null && IsLeafType(elementType) && !IsObjectReferenceType(elementType);
            }

            public static string FormatSummary(object value, Type type)
            {
                if (value == null)
                {
                    return "null";
                }

                type = Nullable.GetUnderlyingType(type) ?? type;
                if (value is IList list)
                {
                    return $"{type.Name} ({list.Count})";
                }

                if (IsObjectReferenceType(type))
                {
                    return Format(value, type);
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

                if (type == typeof(decimal))
                {
                    return ((decimal)value).ToString(CultureInfo.InvariantCulture);
                }

                if (type == typeof(Vector2))
                {
                    var vector = (Vector2)value;
                    return $"[{vector.x:0.###}, {vector.y:0.###}]";
                }

                if (type == typeof(Vector2Int))
                {
                    var vector = (Vector2Int)value;
                    return $"[{vector.x}, {vector.y}]";
                }

                if (type == typeof(Vector3))
                {
                    var vector = (Vector3)value;
                    return $"[{vector.x:0.###}, {vector.y:0.###}, {vector.z:0.###}]";
                }

                if (type == typeof(Vector3Int))
                {
                    var vector = (Vector3Int)value;
                    return $"[{vector.x}, {vector.y}, {vector.z}]";
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

                if (type == typeof(RectInt))
                {
                    var rect = (RectInt)value;
                    return $"[{rect.x}, {rect.y}, {rect.width}, {rect.height}]";
                }

                if (type == typeof(Bounds))
                {
                    var bounds = (Bounds)value;
                    return $"[{bounds.center.x:0.###}, {bounds.center.y:0.###}, {bounds.center.z:0.###}, {bounds.size.x:0.###}, {bounds.size.y:0.###}, {bounds.size.z:0.###}]";
                }

                if (type == typeof(BoundsInt))
                {
                    var bounds = (BoundsInt)value;
                    return $"[{bounds.position.x}, {bounds.position.y}, {bounds.position.z}, {bounds.size.x}, {bounds.size.y}, {bounds.size.z}]";
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

                if (IsObjectReferenceType(targetType) || string.IsNullOrWhiteSpace(rawValue))
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

                if (targetType == typeof(uint) && uint.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var uintValue))
                {
                    value = uintValue;
                    return true;
                }

                if (targetType == typeof(long) && long.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var longValue))
                {
                    value = longValue;
                    return true;
                }

                if (targetType == typeof(ulong) && ulong.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ulongValue))
                {
                    value = ulongValue;
                    return true;
                }

                if (targetType == typeof(short) && short.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var shortValue))
                {
                    value = shortValue;
                    return true;
                }

                if (targetType == typeof(ushort) && ushort.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ushortValue))
                {
                    value = ushortValue;
                    return true;
                }

                if (targetType == typeof(byte) && byte.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var byteValue))
                {
                    value = byteValue;
                    return true;
                }

                if (targetType == typeof(sbyte) && sbyte.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var sbyteValue))
                {
                    value = sbyteValue;
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

                if (targetType == typeof(decimal) && decimal.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var decimalValue))
                {
                    value = decimalValue;
                    return true;
                }

                if (targetType == typeof(char) && rawValue.Length == 1)
                {
                    value = rawValue[0];
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

                if (targetType == typeof(Vector2Int))
                {
                    var values = ParseIntArray(rawValue, 2);
                    if (values == null)
                    {
                        return false;
                    }

                    value = new Vector2Int(values[0], values[1]);
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

                if (targetType == typeof(Vector3Int))
                {
                    var values = ParseIntArray(rawValue, 3);
                    if (values == null)
                    {
                        return false;
                    }

                    value = new Vector3Int(values[0], values[1], values[2]);
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

                    var values = ParseIntArray(rawValue, 3, 4);
                    if (values == null)
                    {
                        return false;
                    }

                    value = values.Length == 4
                        ? new Color32((byte)values[0], (byte)values[1], (byte)values[2], (byte)values[3])
                        : new Color32((byte)values[0], (byte)values[1], (byte)values[2], 255);
                    return true;
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

                if (targetType == typeof(RectInt))
                {
                    var values = ParseIntArray(rawValue, 4);
                    if (values == null)
                    {
                        return false;
                    }

                    value = new RectInt(values[0], values[1], values[2], values[3]);
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

                if (targetType == typeof(BoundsInt))
                {
                    var values = ParseIntArray(rawValue, 6);
                    if (values == null)
                    {
                        return false;
                    }

                    value = new BoundsInt(values[0], values[1], values[2], values[3], values[4], values[5]);
                    return true;
                }

                if (targetType == typeof(LayerMask) && int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var layerValue))
                {
                    value = (LayerMask)layerValue;
                    return true;
                }

                var elementType = GetElementType(targetType);
                if (elementType != null && !IsObjectReferenceType(elementType))
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

            private static int[] ParseIntArray(string rawValue, params int[] allowedSizes)
            {
                var parts = SplitCollection(rawValue);
                if (!allowedSizes.Contains(parts.Count))
                {
                    return null;
                }

                var values = new int[parts.Count];
                for (var index = 0; index < parts.Count; index++)
                {
                    if (!int.TryParse(parts[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out values[index]))
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

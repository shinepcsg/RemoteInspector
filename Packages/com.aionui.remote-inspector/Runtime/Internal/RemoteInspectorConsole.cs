using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Aion.RemoteInspector;
using UnityEngine;

namespace Aion.RemoteInspector.Internal
{
    internal sealed class RemoteInspectorConsole
    {
        private readonly Dictionary<string, CommandDefinition> _commands = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _history = new();

        public RemoteInspectorConsole()
        {
            RegisterBuiltinCommands();
        }

        public void RegisterCommands(Type commandContainerType)
        {
            foreach (var method in commandContainerType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
            {
                var attribute = method.GetCustomAttribute<RemoteCommandAttribute>();
                if (attribute == null)
                {
                    continue;
                }

                var definition = new CommandDefinition
                {
                    Name = method.Name,
                    Alias = attribute.Alias,
                    Category = attribute.Category,
                    Description = string.IsNullOrWhiteSpace(attribute.Description) ? "Custom command." : attribute.Description,
                    Usage = BuildUsage(method),
                    Handler = CreateReflectionHandler(method)
                };

                AddCommand(definition);
            }
        }

        public string Execute(string input)
        {
            var tokens = Tokenize(input);
            if (tokens.Count == 0)
            {
                return string.Empty;
            }

            if (!string.Equals(tokens[0], "h", StringComparison.OrdinalIgnoreCase))
            {
                _history.Add(input);
                if (_history.Count > 64)
                {
                    _history.RemoveAt(0);
                }
            }

            if (string.Equals(tokens[0], "?", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(tokens[0], "help", StringComparison.OrdinalIgnoreCase))
            {
                return tokens.Count > 1 ? DescribeCommand(tokens[1]) : ListCommands();
            }

            if (string.Equals(tokens[0], "h", StringComparison.OrdinalIgnoreCase))
            {
                return string.Join("\n", _history);
            }

            if (!_commands.TryGetValue(tokens[0], out var command))
            {
                throw new InvalidOperationException($"Unknown command '{tokens[0]}'.");
            }

            var args = tokens.Skip(1).ToArray();
            return command.Handler(args);
        }

        private void RegisterBuiltinCommands()
        {
            AddCommand(new CommandDefinition
            {
                Name = "list",
                Alias = "ls",
                Category = "Hierarchy",
                Description = "Lists game objects by wildcard name or [instanceId].",
                Usage = "list <pattern=*>" ,
                Handler = args =>
                {
                    var pattern = args.Length > 0 ? args[0] : "*";
                    var matches = RemoteInspectorIntrospection.FindGameObjectsByPattern(pattern);
                    if (matches.Count == 0)
                    {
                        return "No matching game objects.";
                    }

                    return string.Join("\n", matches.Select(go => $"{go.name} [{go.GetInstanceID()}]"));
                }
            });

            AddCommand(new CommandDefinition
            {
                Name = "comp",
                Alias = "components",
                Category = "Hierarchy",
                Description = "Lists the components on a matching game object.",
                Usage = "comp <pattern>",
                Handler = args =>
                {
                    RequireArgCount(args, 1);
                    var matches = ResolveTargets(args[0]);
                    var output = new StringBuilder();
                    foreach (var gameObject in matches)
                    {
                        output.AppendLine($"{gameObject.name} [{gameObject.GetInstanceID()}]");
                        foreach (var componentName in RemoteInspectorIntrospection.GetComponentNames(gameObject))
                        {
                            output.AppendLine($"  - {componentName}");
                        }
                    }

                    return output.ToString().TrimEnd();
                }
            });

            AddCommand(new CommandDefinition
            {
                Name = "get",
                Alias = "g",
                Category = "Inspection",
                Description = "Reads a property path from a game object or static root.",
                Usage = "get <target.path>",
                Handler = args =>
                {
                    RequireArgCount(args, 1);
                    SplitTargetPath(args[0], out var target, out var memberPath);
                    if (string.IsNullOrWhiteSpace(memberPath))
                    {
                        throw new InvalidOperationException("Expected a property path after the target.");
                    }

                    if (TryGetStatic(target, memberPath, out var staticOutput))
                    {
                        return staticOutput;
                    }

                    var matches = ResolveTargets(target);
                    var lines = new List<string>(matches.Count);
                    foreach (var gameObject in matches)
                    {
                        if (!RemoteInspectorIntrospection.TryGetPathValue(gameObject, memberPath, out var value, out var valueType, out var error))
                        {
                            lines.Add($"{gameObject.name}: {error}");
                            continue;
                        }

                        lines.Add($"{gameObject.name}.{memberPath} = {RemoteInspectorIntrospection.FormatValue(value, valueType)}");
                    }

                    return string.Join("\n", lines);
                }
            });

            AddCommand(new CommandDefinition
            {
                Name = "set",
                Alias = "s",
                Category = "Inspection",
                Description = "Sets a property path on one or more game objects.",
                Usage = "set <target.path> <value>",
                Handler = args =>
                {
                    RequireArgCount(args, 2);
                    SplitTargetPath(args[0], out var target, out var memberPath);
                    if (string.IsNullOrWhiteSpace(memberPath))
                    {
                        throw new InvalidOperationException("Expected a property path after the target.");
                    }

                    if (TrySetStatic(target, memberPath, args[1], out var staticOutput))
                    {
                        return staticOutput;
                    }

                    var matches = ResolveTargets(target);
                    var lines = new List<string>(matches.Count);
                    foreach (var gameObject in matches)
                    {
                        if (!RemoteInspectorIntrospection.TrySetPathValue(gameObject, memberPath, args[1], out var error))
                        {
                            lines.Add($"{gameObject.name}: {error}");
                            continue;
                        }

                        lines.Add($"Updated {gameObject.name}.{memberPath}");
                    }

                    return string.Join("\n", lines);
                }
            });

            AddCommand(new CommandDefinition
            {
                Name = "enable",
                Alias = "e",
                Category = "Hierarchy",
                Description = "Sets matching game objects active.",
                Usage = "enable <pattern>",
                Handler = args =>
                {
                    RequireArgCount(args, 1);
                    var matches = ResolveTargets(args[0]);
                    foreach (var gameObject in matches)
                    {
                        gameObject.SetActive(true);
                    }

                    return $"Enabled {matches.Count} object(s).";
                }
            });

            AddCommand(new CommandDefinition
            {
                Name = "disable",
                Alias = "d",
                Category = "Hierarchy",
                Description = "Disables matching game objects.",
                Usage = "disable <pattern>",
                Handler = args =>
                {
                    RequireArgCount(args, 1);
                    var matches = ResolveTargets(args[0]);
                    foreach (var gameObject in matches)
                    {
                        gameObject.SetActive(false);
                    }

                    return $"Disabled {matches.Count} object(s).";
                }
            });

            AddCommand(new CommandDefinition
            {
                Name = "destroy",
                Alias = "del",
                Category = "Hierarchy",
                Description = "Destroys matching game objects.",
                Usage = "destroy <pattern>",
                Handler = args =>
                {
                    RequireArgCount(args, 1);
                    var matches = ResolveTargets(args[0]);
                    foreach (var gameObject in matches)
                    {
                        UnityEngine.Object.Destroy(gameObject);
                    }

                    return $"Destroyed {matches.Count} object(s).";
                }
            });

            AddCommand(new CommandDefinition
            {
                Name = "move",
                Alias = "mv",
                Category = "Transform",
                Description = "Moves matching game objects to a world position.",
                Usage = "move <pattern> [x, y, z]",
                Handler = args =>
                {
                    RequireArgCount(args, 2);
                    if (!RemoteInspectorIntrospection.TryParseValue(args[1], typeof(Vector3), out var value))
                    {
                        throw new InvalidOperationException("Expected a Vector3 like [0, 1, 2].");
                    }

                    var position = (Vector3)value;
                    var matches = ResolveTargets(args[0]);
                    foreach (var gameObject in matches)
                    {
                        gameObject.transform.position = position;
                    }

                    return $"Moved {matches.Count} object(s) to {position}.";
                }
            });

            AddCommand(new CommandDefinition
            {
                Name = "addcomponent",
                Alias = "addc",
                Category = "Components",
                Description = "Adds a component by type name to matching game objects.",
                Usage = "addcomponent <pattern> <ComponentType>",
                Handler = args =>
                {
                    RequireArgCount(args, 2);
                    var matches = ResolveTargets(args[0]);
                    foreach (var gameObject in matches)
                    {
                        RemoteInspectorIntrospection.AddComponent(new AddComponentRequestPayload
                        {
                            gameObjectInstanceId = gameObject.GetInstanceID(),
                            componentType = args[1]
                        });
                    }

                    return $"Added {args[1]} to {matches.Count} object(s).";
                }
            });
        }

        private void AddCommand(CommandDefinition definition)
        {
            _commands[definition.Name] = definition;
            if (!string.IsNullOrWhiteSpace(definition.Alias))
            {
                _commands[definition.Alias] = definition;
            }
        }

        private string ListCommands()
        {
            var unique = _commands.Values.Distinct().OrderBy(command => command.Category).ThenBy(command => command.Name);
            var output = new StringBuilder();
            foreach (var command in unique)
            {
                output.AppendLine($"{command.Name} ({command.Category}) - {command.Description}");
            }

            return output.ToString().TrimEnd();
        }

        private string DescribeCommand(string name)
        {
            if (!_commands.TryGetValue(name, out var command))
            {
                return $"Unknown command '{name}'.";
            }

            return $"{command.Name}\n{command.Description}\nUsage: {command.Usage}";
        }

        private static void RequireArgCount(string[] args, int minimum)
        {
            if (args.Length < minimum)
            {
                throw new InvalidOperationException("Missing command arguments.");
            }
        }

        private static void SplitTargetPath(string raw, out string target, out string memberPath)
        {
            target = raw;
            memberPath = string.Empty;

            if (raw.StartsWith("[", StringComparison.Ordinal))
            {
                var closingIndex = raw.IndexOf(']');
                if (closingIndex > 0)
                {
                    target = raw.Substring(0, closingIndex + 1);
                    memberPath = closingIndex + 2 <= raw.Length ? raw.Substring(Math.Min(closingIndex + 2, raw.Length)) : string.Empty;
                    return;
                }
            }

            var separatorIndex = raw.IndexOf('.');
            if (separatorIndex > 0)
            {
                target = raw.Substring(0, separatorIndex);
                memberPath = raw.Substring(separatorIndex + 1);
            }
        }

        private static bool TryGetStatic(string target, string memberPath, out string output)
        {
            output = string.Empty;
            if (!RemoteInspectorIntrospection.IsStaticRoot(target))
            {
                return false;
            }

            if (!RemoteInspectorIntrospection.TryGetStaticValue(target, memberPath, out var value, out var valueType, out var error))
            {
                throw new InvalidOperationException(error);
            }

            output = $"{target}.{memberPath} = {RemoteInspectorIntrospection.FormatValue(value, valueType)}";
            return true;
        }

        private static bool TrySetStatic(string target, string memberPath, string rawValue, out string output)
        {
            output = string.Empty;
            if (!RemoteInspectorIntrospection.IsStaticRoot(target))
            {
                return false;
            }

            if (!RemoteInspectorIntrospection.TrySetStaticValue(target, memberPath, rawValue, out var error))
            {
                throw new InvalidOperationException(error);
            }

            output = $"Updated {target}.{memberPath}";
            return true;
        }

        private static List<GameObject> ResolveTargets(string pattern)
        {
            var matches = RemoteInspectorIntrospection.FindGameObjectsByPattern(pattern);
            if (matches.Count == 0)
            {
                throw new InvalidOperationException("No matching game objects.");
            }

            return matches;
        }

        private static List<string> Tokenize(string input)
        {
            var tokens = new List<string>();
            if (string.IsNullOrWhiteSpace(input))
            {
                return tokens;
            }

            var current = new StringBuilder();
            var inQuote = false;
            var bracketDepth = 0;
            foreach (var character in input)
            {
                if (character == '"')
                {
                    inQuote = !inQuote;
                    continue;
                }

                if (!inQuote)
                {
                    if (character == '[')
                    {
                        bracketDepth++;
                    }
                    else if (character == ']')
                    {
                        bracketDepth = Mathf.Max(0, bracketDepth - 1);
                    }
                    else if (char.IsWhiteSpace(character) && bracketDepth == 0)
                    {
                        if (current.Length > 0)
                        {
                            tokens.Add(current.ToString());
                            current.Length = 0;
                        }

                        continue;
                    }
                }

                current.Append(character);
            }

            if (current.Length > 0)
            {
                tokens.Add(current.ToString());
            }

            return tokens;
        }

        private static Func<string[], string> CreateReflectionHandler(MethodInfo method)
        {
            return args =>
            {
                var parameters = method.GetParameters();
                var invokeArgs = new object[parameters.Length];
                for (var index = 0; index < parameters.Length; index++)
                {
                    if (index >= args.Length)
                    {
                        if (parameters[index].HasDefaultValue)
                        {
                            invokeArgs[index] = parameters[index].DefaultValue;
                            continue;
                        }

                        throw new InvalidOperationException($"Missing argument '{parameters[index].Name}'.");
                    }

                    if (!RemoteInspectorIntrospection.TryParseValue(args[index], parameters[index].ParameterType, out invokeArgs[index]))
                    {
                        throw new InvalidOperationException($"Could not convert '{args[index]}' to {parameters[index].ParameterType.Name}.");
                    }
                }

                var result = method.Invoke(null, invokeArgs);
                return result switch
                {
                    null => "OK",
                    string text => text,
                    _ => result.ToString()
                };
            };
        }

        private static string BuildUsage(MethodInfo method)
        {
            var parameters = method.GetParameters();
            if (parameters.Length == 0)
            {
                return method.Name;
            }

            return method.Name + " " + string.Join(" ", parameters.Select(parameter => $"<{parameter.Name}>"));
        }

        private sealed class CommandDefinition
        {
            public string Name;
            public string Alias;
            public string Category;
            public string Description;
            public string Usage;
            public Func<string[], string> Handler;
        }
    }
}

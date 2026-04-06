# Remote Inspector

A Unity 6000 package that exposes a lightweight runtime inspector, log viewer, and command console through a browser over WebSocket.

## Included

- Scene hierarchy browser
- GameObject and component inspection
- Field editing for common serializable types
- Deeper nested field and collection element editing
- Real-time log streaming
- Remote command console with extensible custom commands
- Embedded browser UI served by the runtime
- Runtime status overlay and control panel
- Optional HTTPS/WSS transport with PFX or generated self-signed certificate

## Quick Start

1. Add the package folder to your Unity project's `Packages` directory.
2. Create an empty `GameObject` in your startup scene.
3. Add the `RemoteInspectorBehaviour` component.
4. Set a port and password.
5. Enter Play Mode and open `http://127.0.0.1:<port>`.

## Runtime Notes

- The transport is a custom TCP server that serves HTTP and upgrades to WebSocket on `/ws`.
- The current implementation targets Unity Editor and Standalone players.
- HTTPS/WSS is supported through a provided PFX certificate or a generated self-signed certificate.
- Browsers will require certificate trust when using a generated self-signed certificate.
- Editing supports nested serializable fields, collection elements, vectors, colors, rects, bounds, and value-type submembers such as `Vector3.x`.

## Custom Console Commands

Use the `RemoteCommandAttribute` on a static method and register the type:

```csharp
using RemoteInspector;
using UnityEngine;

public static class DemoCommands
{
    [RemoteCommand("Demo", "tp", "Teleports a game object by name.")]
    public static string Teleport(string name, Vector3 position)
    {
        var go = GameObject.Find(name);
        if (go == null)
        {
            throw new System.Exception("GameObject not found.");
        }

        go.transform.position = position;
        return $"Moved {go.name} to {position}.";
    }
}

public sealed class DemoBootstrap : MonoBehaviour
{
    private void Awake()
    {
        RemoteInspectorBehaviour.RegisterCommands<DemoCommands>();
    }
}
```

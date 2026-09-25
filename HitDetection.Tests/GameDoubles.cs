using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NumericsQuaternion = System.Numerics.Quaternion;
using NumericsVector = System.Numerics.Vector3;

// Small engine doubles let the production detector and shotgun code run without Unity.
// They cover transforms, component attachment, and death requests, not physics or rendering.
namespace UnityEngine
{
    internal readonly struct Vector3
    {
        public readonly float x;
        public readonly float y;
        public readonly float z;

        public float magnitude => Value.Length();
        public static Vector3 right   => new(1, 0, 0);
        public static Vector3 up      => new(0, 1, 0);
        public static Vector3 forward => new(0, 0, 1);
        internal NumericsVector Value => new(x, y, z);

        public Vector3(float x, float y, float z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }

        public static Vector3 operator +(Vector3 a, Vector3 b) => new(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new(a.x - b.x, a.y - b.y, a.z - b.z);
        public static Vector3 operator *(Vector3 value, float scale) => new(value.x * scale, value.y * scale, value.z * scale);
        internal static Vector3 From(NumericsVector value) => new(value.X, value.Y, value.Z);
    }

    internal readonly struct Quaternion
    {
        internal readonly NumericsQuaternion Value;

        public static Quaternion identity => new(NumericsQuaternion.Identity);

        private Quaternion(NumericsQuaternion value)
        {
            Value = value;
        }

        public static Quaternion AngleAxis(float degrees, Vector3 axis) => new(NumericsQuaternion.CreateFromAxisAngle(NumericsVector.Normalize(axis.Value), degrees * Mathf.Deg2Rad));
        public static Quaternion Inverse(Quaternion value) => new(NumericsQuaternion.Inverse(value.Value));
        public static Quaternion operator *(Quaternion a, Quaternion b) => new(a.Value * b.Value);
        public static Vector3 operator *(Quaternion rotation, Vector3 value) => Vector3.From(NumericsVector.Transform(value.Value, rotation.Value));

        public static Quaternion LookRotation(Vector3 forward)
        {
            var z = NumericsVector.Normalize(forward.Value);
            var x = NumericsVector.Normalize(NumericsVector.Cross(NumericsVector.UnitY, z));
            var y = NumericsVector.Cross(z, x);
            return new Quaternion(NumericsQuaternion.CreateFromRotationMatrix(new System.Numerics.Matrix4x4(
                x.X, x.Y, x.Z, 0, y.X, y.Y, y.Z, 0, z.X, z.Y, z.Z, 0, 0, 0, 0, 1)));
        }

        public static Quaternion FromToRotation(Vector3 from, Vector3 to)
        {
            var first  = NumericsVector.Normalize(from.Value);
            var second = NumericsVector.Normalize(to.Value);
            return new Quaternion(NumericsQuaternion.Normalize(new NumericsQuaternion(NumericsVector.Cross(first, second), 1 + NumericsVector.Dot(first, second))));
        }
    }

    internal sealed class Transform
    {
        public Vector3    position;
        public Quaternion rotation = Quaternion.identity;
        public Vector3    lossyScale = new(1, 1, 1);
        public Vector3 right => rotation * Vector3.right;

        public Vector3 InverseTransformPoint(Vector3 point) => InverseTransformVector(point - position);

        public Vector3 InverseTransformVector(Vector3 vector)
        {
            var local = Quaternion.Inverse(rotation) * vector;
            return new Vector3(local.x / lossyScale.x, local.y / lossyScale.y, local.z / lossyScale.z);
        }
    }

    internal class Object
    {
        public static readonly List<GameObject> Clones = new();

        private bool _destroyed;

        public static implicit operator bool(Object value) => value is not null && !value._destroyed;

        public static GameObject Instantiate(GameObject original, Vector3 position, Quaternion rotation)
        {
            var clone = new GameObject();
            clone.transform.position = position;
            clone.transform.rotation = rotation;
            foreach (var component in original.Components)
            {
                clone.AddComponent(component.GetType());
            }

            Clones.Add(clone);
            return clone;
        }

        public static void Destroy(GameObject gameObject, float delay = 0)
        {
            if (delay == 0)
            {
                gameObject._destroyed = true;
                gameObject.activeInHierarchy = false;
                foreach (var component in gameObject.Components)
                {
                    component._destroyed = true;
                }
            }
        }
    }

    internal sealed class GameObject : Object
    {
        public string name;
        public bool activeInHierarchy = true;
        public readonly Transform transform = new();
        internal readonly List<Component> Components = new();

        public T AddComponent<T>() where T : Component, new() => (T)AddComponent(typeof(T));
        public T GetComponent<T>() where T : Component => Components.OfType<T>().FirstOrDefault();
        public T[] GetComponentsInChildren<T>(bool includeInactive) where T : Component => Components.OfType<T>().ToArray();

        internal Component AddComponent(Type type)
        {
            var component = (Component)Activator.CreateInstance(type);
            component.gameObject = this;
            Components.Add(component);
            return component;
        }
    }

    internal class Component : Object
    {
        public GameObject gameObject;
        public Transform transform => gameObject.transform;

        public T GetComponent<T>() where T : Component => gameObject.GetComponent<T>();
        public T[] GetComponentsInChildren<T>(bool includeInactive) where T : Component => gameObject.GetComponentsInChildren<T>(includeInactive);
    }

    internal class MonoBehaviour : Component
    {
        public bool enabled = true;
    }

    internal class Collider : Component
    {
        public bool enabled = true;
    }

    internal sealed class CapsuleCollider : Collider
    {
        public float   radius    = 0.5f;
        public float   height    = 2f;
        public int     direction = 1;
        public Vector3 center    = default;
    }

    internal static class Physics
    {
        public static void IgnoreCollision(Collider first, Collider second) {}
    }

    internal static class Mathf
    {
        public const float Deg2Rad = MathF.PI / 180;

        public static float Abs(float value) => MathF.Abs(value);
        public static float Min(float first, float second) => MathF.Min(first, second);
        public static float Min(params float[] values) => values.Min();
        public static float Max(float first, float second) => MathF.Max(first, second);
    }
}

internal enum DeathType
{
    Impact,
    Crushed,
    Asphyxiation,
    Energy,
    Lava,
    Digestion
}

internal sealed class OWRigidbody : UnityEngine.Component
{
    public event Action<OWRigidbody> OnWarpOWRigidbody;

    private UnityEngine.Vector3 _velocity;
    private UnityEngine.Vector3 _angularVelocity;

    public UnityEngine.Vector3 GetPosition() => transform.position;
    public UnityEngine.Vector3 GetVelocity() => _velocity;
    public UnityEngine.Vector3 GetAngularVelocity() => _angularVelocity;
    public void SetVelocity(UnityEngine.Vector3 velocity) => _velocity = velocity;
    public void SetAngularVelocity(UnityEngine.Vector3 velocity) => _angularVelocity = velocity;

    public void Warp(UnityEngine.Vector3 position)
    {
        transform.position = position;
        OnWarpOWRigidbody?.Invoke(this);
    }
}

internal sealed class PlayerCharacterController
{
    public UnityEngine.Collider _bodyCollider;
}

internal sealed class DeathManager : UnityEngine.MonoBehaviour
{
    public int       KillCount { get; private set; }
    public DeathType LastDeath { get; private set; }
    public float     ImpactSpeed { get; private set; }

    public bool IsPlayerDying() => KillCount > 0;
    public bool IsPlayerDead() => KillCount > 0;
    public void SetImpactDeathSpeed(float speed) => ImpactSpeed = speed;

    public void KillPlayer(DeathType type)
    {
        KillCount++;
        LastDeath = type;
    }
}

internal static class Locator
{
    public static OWRigidbody               Player;
    public static PlayerCharacterController Controller;
    public static DeathManager              DeathManager;

    public static OWRigidbody GetPlayerBody() => Player;
    public static PlayerCharacterController GetPlayerController() => Controller;
    public static DeathManager GetDeathManager() => DeathManager;
}

namespace HarmonyLib
{
    internal static class AccessTools
    {
        public static FieldInfo Field(Type type, string name) => type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
    }
}

namespace OWML.Common
{
    internal enum MessageType
    {
        Info,
        Warning,
        Error
    }
}

namespace ProbeRoulette
{
    internal sealed class ProbeRouletteMod : UnityEngine.MonoBehaviour
    {
        public bool      TargetingEnabled   = true;
        public bool      PlayerHitsKill     = true;
        public float     ProbeHitRadius     = 20f;
        public DeathType PlayerHitDeathType = DeathType.Impact;
        public float     ShotgunSpread      = 5f;
        public int       ShotgunProbeCount  = 9;
        public readonly List<string> Errors = new();

        public void Log(string message, OWML.Common.MessageType type = OWML.Common.MessageType.Info)
        {
            if (type == OWML.Common.MessageType.Error)
            {
                Errors.Add(message);
            }
        }
    }
}

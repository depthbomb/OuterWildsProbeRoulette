using HarmonyLib;
using OWML.Common;
using ProbeRoulette.Core;
using UnityEngine;
using Vector = ProbeRoulette.Core.Vector;

namespace ProbeRoulette;

internal sealed class ProbePlayerHitDetector : MonoBehaviour
{
    private CapsuleCollider  _capsule;
    private Transform        _capsuleTransform;
    private DeathManager     _deathManager;
    private ProbeRouletteMod _mod;
    private OWRigidbody      _player;
    private Vector3          _previousRelativePosition;
    private OWRigidbody      _probe;
    private bool             _tracking;

    private void FixedUpdate()
    {
        var manager = _deathManager;
        if (!_mod || !_probe || !_player || !_capsule || !manager || manager.IsPlayerDying() || manager.IsPlayerDead())
        {
            enabled = false;

            return;
        }

        // Relative positions cancel world-origin shifts and movement without storing the player's rotation.
        var current = _probe.GetPosition() - _capsuleTransform.position;
        if (!_mod.TargetingEnabled || !_mod.PlayerHitsKill || !_capsule.enabled || !_capsule.gameObject.activeInHierarchy)
        {
            _tracking = false;

            return;
        }

        if (!_tracking)
        {
            _previousRelativePosition = current;
            _tracking                 = true;

            return;
        }

        var axis         = _capsule.direction == 0 ? Vector3.right : _capsule.direction == 2 ? Vector3.forward : Vector3.up;
        var half         = Mathf.Max(0f, _capsule.height * 0.5f - _capsule.radius);
        var from         = _capsule.center - axis * half;
        var to           = _capsule.center + axis * half;
        var scale        = _capsuleTransform.lossyScale;
        var minimumScale = Mathf.Max(0.001f, Mathf.Min(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
        var radius       = _capsule.radius + _mod.ProbeHitRadius / minimumScale;
        // Use the same rotation and scale for both endpoints, even if the player turned since the last tick.
        var start        = _capsuleTransform.InverseTransformVector(_previousRelativePosition);
        var end          = _capsuleTransform.InverseTransformVector(current);
        // At this speed the probe can cross the whole player between frames. Check the path, too.
        var hit          = HitGeometry.SweptSphereHitsCapsule(ToVector(start), ToVector(end), ToVector(from), ToVector(to), radius);
        _previousRelativePosition = current;
        if (!hit)
        {
            return;
        }

        var speed = (_probe.GetVelocity() - _player.GetVelocity()).magnitude;
        var type  = _mod.PlayerHitDeathType;
        if (type == DeathType.Impact)
        {
            // The death animation uses this speed to pick the impact effects.
            manager.SetImpactDeathSpeed(speed);
        }

        _mod.Log($"Probe hit player at {speed:F1}m/s; requesting {type} death.");
        manager.KillPlayer(type);
        enabled = false;
    }

    private void OnDisable()
    {
        _tracking = false;
    }

    private void OnDestroy()
    {
        if (_player != null)
        {
            _player.OnWarpOWRigidbody -= OnBodyWarped;
        }

        if (_probe != null)
        {
            _probe.OnWarpOWRigidbody -= OnBodyWarped;
        }
    }

    public void Initialize(OWRigidbody probe, ProbeRouletteMod mod)
    {
        _probe  = probe;
        _mod    = mod;
        _player = Locator.GetPlayerBody();

        var controller = Locator.GetPlayerController();
        _capsule = controller != null
            ? AccessTools.Field(typeof(PlayerCharacterController), "_bodyCollider")?.GetValue(controller) as CapsuleCollider
            : null;
        if (_player == null || _capsule == null)
        {
            mod.Log("Player hit detection unavailable: player body capsule was not found.", MessageType.Warning);
            enabled = false;

            return;
        }

        _capsuleTransform          =  _capsule.transform;
        _deathManager              =  Locator.GetDeathManager();
        _previousRelativePosition  =  probe.GetPosition() - _capsuleTransform.position;
        _tracking                  =  true;
        _player.OnWarpOWRigidbody  += OnBodyWarped;
        _probe.OnWarpOWRigidbody   += OnBodyWarped;
        mod.Log($"Player hit detection armed: {mod.PlayerHitDeathType}; probe radius {mod.ProbeHitRadius:F2}m.");
    }

    private void OnBodyWarped(OWRigidbody body)
    {
        // A teleport isn't a collision path. Start a fresh sweep at the new position.
        _tracking = false;
    }

    private static Vector ToVector(Vector3 value) => new(value.x, value.y, value.z);
}

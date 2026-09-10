using ProbeRoulette.Core;
using System;
using System.Collections.Generic;
using UnityEngine;
using Random = System.Random;
using Vector = ProbeRoulette.Core.Vector;

namespace ProbeRoulette;

internal sealed class LaunchTargeting : MonoBehaviour
{
    private const float                        LaunchBoost = 500f;
    private const float                        Horizon     = 45f;
    private       float                        _arrivalTime;
    private       DebrisPose[]                 _assembledDebris;
    private       OrbitalProbeLaunchController _cannon;
    private       float                        _closestDistance = float.PositiveInfinity;
    private       float                        _closestTime;
    private       OWRigidbody[]                _debris;
    private       float                        _launchTime;
    private       Vector3                      _localAimPoint;

    private ProbeRouletteMod _mod;
    private bool             _monitoring;
    private bool             _prepared;
    private Vector3          _previousSeparation;
    private OWRigidbody      _probe;
    private ShotTarget       _selection;
    private OWRigidbody      _target;

    private void FixedUpdate()
    {
        if (!_monitoring)
        {
            return;
        }

        if (_probe == null || _target == null)
        {
            FinishMonitoring("probe or target no longer available");

            return;
        }

        var separation = _target.transform.TransformPoint(_localAimPoint) - _probe.GetPosition();
        var segment    = separation                                       - _previousSeparation;
        var weight = segment.sqrMagnitude > 0.000001f
            ? Mathf.Clamp01(-Vector3.Dot(_previousSeparation, segment) / segment.sqrMagnitude)
            : 0f;
        var distance = (_previousSeparation + segment * weight).magnitude;
        if (distance < _closestDistance)
        {
            _closestDistance = distance;
            _closestTime     = Mathf.Max(0f, Time.time - _launchTime - Time.fixedDeltaTime * (1f - weight));
        }

        _previousSeparation = separation;

        var elapsed = Time.time - _launchTime;
        if (elapsed > _arrivalTime + 3f)
        {
            FinishMonitoring("observation complete");
        }
    }

    private void LateUpdate()
    {
        if (!_prepared)
        {
            RestoreDebrisAssembly();
        }
    }

    public void CompleteLaunch()
    {
        _prepared = true;
        if (!_monitoring)
        {
            // No need to keep getting Unity callbacks for the rest of the loop.
            enabled = false;
        }
    }

    public void Initialize(OrbitalProbeLaunchController cannon,
                           OWRigidbody                  probe,
                           OWRigidbody[]                debris,
                           int                          loop,
                           ProbeRouletteMod             mod)
    {
        _cannon = cannon;
        _probe  = probe;
        _debris = debris ?? new OWRigidbody[0];
        _mod    = mod;

        // Our coin flips shouldn't change the game's other random events.
        var random = new Random(Guid.NewGuid().GetHashCode());
        _selection = Targeting.Select(loop, mod.FirstLoop, mod.Chance, random.NextDouble(), random.NextDouble(), mod.Selection);
        if (_selection == ShotTarget.Normal || _probe == null)
        {
            enabled = false;

            return;
        }

        _target = _selection == ShotTarget.Player ? Locator.GetPlayerBody() : Locator.GetShipBody();
        if (_target == null)
        {
            mod.Log($"Loop {loop}: {_selection} unavailable; keeping normal shot.");
            enabled = false;

            return;
        }

        // The player capsule is more useful than the root at the character's feet.
        var capsule = _target.GetComponentInChildren<CapsuleCollider>();
        var aimPoint = _selection == ShotTarget.Player && capsule != null
            ? capsule.transform.TransformPoint(capsule.center)
            : _target.GetWorldCenterOfMass();
        _localAimPoint = _target.transform.InverseTransformPoint(aimPoint);
        CaptureDebrisAssembly();
        mod.Log($"Loop {loop}: selected {_selection} ({mod.Chance:0.#}% targeted chance).");

        // Awake has already detached these bodies, so rotating just the parent won't move them.
        var direction = aimPoint - cannon.transform.position;
        if (direction.sqrMagnitude > 0.001f)
        {
            RotateAssembly(Quaternion.FromToRotation(cannon.transform.forward, direction.normalized));
        }
    }

    public void PrepareLaunch()
    {
        if (_prepared || !enabled || _target == null || _probe == null || !_mod.TargetingEnabled)
        {
            return;
        }

        _prepared = true;
        RestoreDebrisAssembly();
        var parent = _probe.GetOrigParentBody();
        if (parent == null || parent.transform != _cannon.transform)
        {
            _mod.Log("Unexpected probe parent; keeping existing launch.");

            return;
        }

        var step       = Mathf.Max(Time.fixedDeltaTime, 0.001f);
        var count      = (int)Math.Ceiling(Horizon / step) + 1;
        var pivot      = _cannon.transform.position;
        var point      = _target.transform.TransformPoint(_localAimPoint);
        var trajectory = PredictTarget(pivot, point, step, count);
        var inherited  = ToVector(_probe.GetVelocity());
        var offset     = _probe.GetPosition() - pivot;
        var rotation   = Quaternion.identity;
        var solution   = default(Intercept);

        // The probe sits out at the muzzle. Turning the cannon moves that starting point too.
        for (var iteration = 0; iteration < 6; iteration++)
        {
            var origin = ToVector(rotation * offset);
            if (!Targeting.TryIntercept(trajectory, step, origin, inherited, LaunchBoost, out solution))
            {
                _mod.Log($"No {_selection} intercept within {Horizon:0}s; retaining direct aim.");

                return;
            }

            rotation = Quaternion.FromToRotation(_cannon.transform.forward, ToUnity(solution.Direction));
        }

        RotateAssembly(rotation);
        RestoreDebrisAssembly();
        _launchTime         = Time.time;
        _arrivalTime        = (float)solution.Time;
        _previousSeparation = point - _probe.GetPosition();
        _monitoring         = _mod.LogApproach;
        _mod.Log($"Firing at {_selection}; predicted arrival {_arrivalTime:F3}s; " +
                 $"range {_previousSeparation.magnitude:F1}m; inherited speed {inherited.Length:F2}m/s. No homing.");
    }

    private void CaptureDebrisAssembly()
    {
        // Physics hasn't started yet, so this is our chance to save the intact assembly.
        var poses = new List<DebrisPose>(_debris.Length);
        foreach (var body in _debris)
        {
            if (body != null && body != _probe)
            {
                poses.Add(new DebrisPose(body, _cannon.transform));
            }
        }

        _assembledDebris = poses.ToArray();
        _mod.Log($"Holding {_assembledDebris.Length} cannon debris pieces assembled until launch.");
    }

    private void RestoreDebrisAssembly()
    {
        if (_assembledDebris == null || _cannon == null)
        {
            return;
        }

        foreach (var pose in _assembledDebris)
        {
            if (pose.Body == null)
            {
                continue;
            }

            // Hold the pose, but let the game keep its velocity and spin ready for the breakup.
            pose.Body.SetPosition(_cannon.transform.TransformPoint(pose.Position));
            pose.Body.SetRotation(_cannon.transform.rotation * pose.Rotation);
        }
    }

    private Vector[] PredictTarget(Vector3 origin, Vector3 aimPoint, float step, int count)
    {
        var hearth = Locator.GetAstroObject(AstroObject.Name.TimberHearth)?.GetOWRigidbody();
        var sun    = Locator.GetAstroObject(AstroObject.Name.Sun)?.GetOWRigidbody();
        var field  = sun != null ? sun.GetAttachedGravityVolume() : null;
        if (hearth == null || sun == null || field == null || (aimPoint - hearth.GetPosition()).magnitude > 500f)
        {
            _mod.Log("Using constant-velocity prediction outside Timber Hearth's surface region.");

            return Trajectory.Linear(ToVector(aimPoint - origin), ToVector(_target.GetPointVelocity(aimPoint)), step, count);
        }

        var surfaceVelocity = _target.GetPointVelocity(aimPoint) - hearth.GetPointVelocity(aimPoint);
        if (surfaceVelocity.magnitude < 1f)
        {
            surfaceVelocity = Vector3.zero;
        }

        return Trajectory.Surface(ToVector(hearth.GetWorldCenterOfMass() - origin), ToVector(hearth.GetVelocity()),
            ToVector(sun.GetWorldCenterOfMass()                          - origin), ToVector(sun.GetVelocity()),
            ToVector(aimPoint                                            - hearth.GetWorldCenterOfMass()), ToVector(hearth.GetAngularVelocity()),
            ToVector(surfaceVelocity), radius => field.CalculateGravityMagnitude((float)radius), step, count);
    }

    private void RotateAssembly(Quaternion rotation)
    {
        var pivot = _cannon.transform.position;
        RotateDetachedBody(_probe, pivot, rotation);
        foreach (var body in _debris)
        {
            if (body != null && body != _probe)
            {
                RotateDetachedBody(body, pivot, rotation);
            }
        }

        _cannon.transform.rotation = rotation * _cannon.transform.rotation;
    }

    private void RotateDetachedBody(OWRigidbody body, Vector3 pivot, Quaternion rotation)
    {
        if (body == null || body.transform.IsChildOf(_cannon.transform))
        {
            return;
        }

        body.SetPosition(pivot + rotation * (body.GetPosition() - pivot));
        body.SetRotation(rotation * body.transform.rotation);
    }

    private void FinishMonitoring(string reason)
    {
        _mod.Log($"{_selection} closest approach: {_closestDistance:F3}m at {_closestTime:F3}s " +
                 $"(predicted {_arrivalTime:F3}s; {reason}). Distance is to the aim point, not a collision report.");
        _monitoring = false;
        enabled     = false;
    }

    private static Vector ToVector(Vector3 value) => new(value.x, value.y, value.z);

    private static Vector3 ToUnity(Vector value) => new((float)value.X, (float)value.Y, (float)value.Z);

    private readonly struct DebrisPose
    {
        public readonly OWRigidbody Body;
        public readonly Vector3     Position;
        public readonly Quaternion  Rotation;

        public DebrisPose(OWRigidbody body, Transform cannon)
        {
            Body     = body;
            Position = cannon.InverseTransformPoint(body.transform.position);
            Rotation = Quaternion.Inverse(cannon.rotation) * body.transform.rotation;
        }
    }
}

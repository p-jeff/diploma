using _Projects.HandPose;
using UnityEngine;

/// <summary>
/// Bridges a two-handed gesture setup to a single FollowTransform.
/// Place on any GameObject and wire each hand's SmallHeart _whenSelected event:
///   1. HandPoseTargetInjector.InjectLeft()  (or InjectRight())
///   2. HandPoseAnimation.OnPoseSelected()
/// </summary>
public class HandPoseTargetInjector : MonoBehaviour
{
    [SerializeField] private FollowTransform followTransform;
    [SerializeField] private Transform leftTarget;
    [SerializeField] private Transform rightTarget;

    public void InjectLeft()  => followTransform.SetTarget(leftTarget);
    public void InjectRight() => followTransform.SetTarget(rightTarget);
}

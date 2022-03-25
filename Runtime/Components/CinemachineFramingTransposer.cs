using System;
using Cinemachine.Utility;
using UnityEngine;
using UnityEngine.Serialization;

namespace Cinemachine
{
    /// <summary>
    /// This is a Cinemachine Component in the Body section of the component pipeline.
    /// Its job is to position the camera in a fixed screen-space relationship to
    /// the vcam's Follow target object, with offsets and damping.
    ///
    /// The camera will be first moved along the camera Z axis until the Follow target
    /// is at the desired distance from the camera's X-Y plane.  The camera will then
    /// be moved in its XY plane until the Follow target is at the desired point on
    /// the camera's screen.
    ///
    /// The FramingTransposer will only change the camera's position in space.  It will not
    /// re-orient or otherwise aim the camera.
    ///
    /// For this component to work properly, the vcam's LookAt target must be null.
    /// The Follow target will define what the camera is looking at.
    ///
    /// If the Follow target is a ICinemachineTargetGroup, then additional controls will
    /// be available to dynamically adjust the camera's view in order to frame the entire group.
    ///
    /// Although this component was designed for orthographic cameras, it works equally
    /// well with perspective cameras and can be used in 3D environments.
    /// </summary>
    [DocumentationSorting(DocumentationSortingAttribute.Level.UserRef)]
    [AddComponentMenu("")] // Don't display in add component menu
    [SaveDuringPlay]
    [CameraPipelineAttribute(CinemachineCore.Stage.Body)]
    public class CinemachineFramingTransposer : CinemachineComponentBase
    {
        /// <summary>
        /// Offset from the Follow Target object (in target-local co-ordinates).  The camera will attempt to
        /// frame the point which is the target's position plus this offset.  Use it to correct for
        /// cases when the target's origin is not the point of interest for the camera.
        /// </summary>
        [Tooltip("Offset from the Follow Target object (in target-local co-ordinates).  "
            + "The camera will attempt to frame the point which is the target's position plus "
            + "this offset.  Use it to correct for cases when the target's origin is not the "
            + "point of interest for the camera.")]
        public Vector3 m_TrackedObjectOffset;

        /// <summary>This setting will instruct the composer to adjust its target offset based
        /// on the motion of the target.  The composer will look at a point where it estimates
        /// the target will be this many seconds into the future.  Note that this setting is sensitive
        /// to noisy animation, and can amplify the noise, resulting in undesirable camera jitter.
        /// If the camera jitters unacceptably when the target is in motion, turn down this setting,
        /// or animate the target more smoothly.</summary>
        [Tooltip("This setting will instruct the composer to adjust its target offset based on the "
            + "motion of the target.  The composer will look at a point where it estimates the target "
            + "will be this many seconds into the future.  Note that this setting is sensitive to noisy "
            + "animation, and can amplify the noise, resulting in undesirable camera jitter.  "
            + "If the camera jitters unacceptably when the target is in motion, turn down this "
            + "setting, or animate the target more smoothly.")]
        [Range(0f, 1f)]
        [Space]
        public float m_LookaheadTime = 0;

        /// <summary>Controls the smoothness of the lookahead algorithm.  Larger values smooth out
        /// jittery predictions and also increase prediction lag</summary>
        [Tooltip("Controls the smoothness of the lookahead algorithm.  Larger values smooth out "
            + "jittery predictions and also increase prediction lag")]
        [Range(0, 30)]
        public float m_LookaheadSmoothing = 0;

        /// <summary>If checked, movement along the Y axis will be ignored for lookahead calculations</summary>
        [Tooltip("If checked, movement along the Y axis will be ignored for lookahead calculations")]
        public bool m_LookaheadIgnoreY;

        /// <summary>How aggressively the camera tries to maintain the offset in the X, Y, and Z axes.
        /// Small numbers are more responsive. Larger numbers give a more heavy slowly responding camera.
        /// Using different settings per axis can yield a wide range of camera behaviors.</summary>
        [Space]
        [Tooltip("How aggressively the camera tries to maintain the offset in the X, Y, and Z axes.  "
            + "Small numbers are more responsive. Larger numbers give a more heavy slowly responding camera.  "
            + "Using different settings per axis can yield a wide range of camera behaviors.")]
        public Vector3 m_Damping = Vector3.one;
        
        /// <summary>If set, damping will apply only to target motion, and not when 
        /// the camera rotation changes.  Turn this on to get an instant response when 
        /// the rotation changes</summary>
        [Tooltip("If set, damping will apply  only to target motion, but not to camera "
            + "rotation changes.  Turn this on to get an instant response when the rotation changes.  ")]
        public bool m_TargetMovementOnly = true;

        /// <summary>Horizontal and Vertical screen position for target. The camera will move to position the tracked object here.</summary>
        [Space]
        [Tooltip("Horizontal and Vertical screen position for target. The camera will move to position the tracked object here.")]
        public Vector2 m_Screen = new Vector2(0.5f, 0.5f);

        /// <summary>The distance along the camera axis that will be maintained from the Follow target</summary>
        [Tooltip("The distance along the camera axis that will be maintained from the Follow target")]
        public float m_CameraDistance = 10f;

        /// <summary>Camera will not move if the target is within the dead zone.</summary>
        [Space]
        [Tooltip("Camera will not move if the target is within the dead zone.")]
        public Vector3 m_DeadZone;

        /// <summary>If checked, then then soft zone will be unlimited in size</summary>
        [Space]
        [Tooltip("If checked, then then soft zone will be unlimited in size.")]
        public bool m_UnlimitedSoftZone = false;

        /// <summary>When target is within this region, camera will gradually move to re-align
        /// towards the desired position, depending onm the damping speed</summary>
        [Tooltip("When target is within this region, camera will gradually move horizontally to "
            + "re-align towards the desired position, depending on the damping speed.")]
        public Vector2 m_SoftZoneSize = new Vector2(0.8f, 0.8f);

        /// <summary>A non-zero bias will move the targt position away from the center of the soft zone</summary>
        [Tooltip("A non-zero bias will move the target position away from the center of the soft zone.")]
        public Vector2 m_SoftZoneBias;

        /// <summary>Force target to center of screen when this camera activates.
        /// If false, will clamp target to the edges of the dead zone</summary>
        [Tooltip("Force target to center of screen when this camera activates.  "
            + "If false, will clamp target to the edges of the dead zone")]
        public bool m_CenterOnActivate = true;

        /// <summary>What screen dimensions to consider when framing</summary>
        [DocumentationSorting(DocumentationSortingAttribute.Level.UserRef)]
        public enum FramingMode
        {
            /// <summary>Consider only the horizontal dimension.  Vertical framing is ignored.</summary>
            Horizontal,
            /// <summary>Consider only the vertical dimension.  Horizontal framing is ignored.</summary>
            Vertical,
            /// <summary>The larger of the horizontal and vertical dimensions will dominate, to get the best fit.</summary>
            HorizontalAndVertical,
            /// <summary>Don't do any framing adjustment</summary>
            None
        };

        /// <summary>What screen dimensions to consider when framing</summary>
        [Space]
        [Tooltip("What screen dimensions to consider when framing.  Can be Horizontal, Vertical, or both")]
        [FormerlySerializedAs("m_FramingMode")]
        public FramingMode m_GroupFramingMode = FramingMode.HorizontalAndVertical;

        /// <summary>How to adjust the camera to get the desired framing</summary>
        public enum AdjustmentMode
        {
            /// <summary>Do not move the camera, only adjust the FOV.</summary>
            ZoomOnly,
            /// <summary>Just move the camera, don't change the FOV.</summary>
            DollyOnly,
            /// <summary>Move the camera as much as permitted by the ranges, then
            /// adjust the FOV if necessary to make the shot.</summary>
            DollyThenZoom
        };

        /// <summary>How to adjust the camera to get the desired framing</summary>
        [Tooltip("How to adjust the camera to get the desired framing.  You can zoom, dolly in/out, or do both.")]
        public AdjustmentMode m_AdjustmentMode = AdjustmentMode.ZoomOnly;

        // TODO: GroupFraming should be within one struct -> GroupFramingState
        /// <summary>How much of the screen to fill with the bounding box of the targets.</summary>
        [Tooltip("The bounding box of the targets should occupy this amount of the screen space.  "
            + "1 means fill the whole screen.  0.5 means fill half the screen, etc.")]
        [Range(0.01f, 2f)]
        public float m_GroupFramingSize = 0.8f;

        /// <summary>The maximum distance the camera can move towards the target and away from the target.</summary>
        [Tooltip("The maximum distance the camera can move towards the target and away from the target.")]
        public Vector2 m_MaxDolly = new Vector2(50f, 50f);

        /// <summary>The minimum and maximum distance to the target the camera can get.</summary>
        [Tooltip("The minimum and maximum distance to the target the camera can get.")]
        public Vector2 m_DistanceRange = new Vector2(1f, 50f);

        /// <summary>When zooming, the fov will stay within this range.</summary>
        [Tooltip("When zooming, the fov will stay within this range.")]
        public Vector2 m_FOVRange = new Vector2(20, 80);

        [Tooltip("When zooming, the orthographic will stay within this range.")]
        public Vector2 m_OrthoSizeRange = new Vector2(1, 100f);

        /// <summary>Internal API for the inspector editor</summary>
        internal Rect SoftGuideRect
        {
            get
            {
                return new Rect(
                    m_Screen.x - m_DeadZone.x / 2, m_Screen.y - m_DeadZone.y / 2,
                    m_DeadZone.x, m_DeadZone.y);
            }
            set
            {
                m_DeadZone.x = Mathf.Clamp(value.width, 0, 2);
                m_DeadZone.y = Mathf.Clamp(value.height, 0, 2);
                m_Screen.x = Mathf.Clamp(value.x + m_DeadZone.x / 2, -0.5f,  1.5f);
                m_Screen.y = Mathf.Clamp(value.y + m_DeadZone.y / 2, -0.5f,  1.5f);
                m_SoftZoneSize.x = Mathf.Max(m_SoftZoneSize.x, m_DeadZone.x);
                m_SoftZoneSize.y = Mathf.Max(m_SoftZoneSize.y, m_DeadZone.y);
            }
        }

        /// <summary>Internal API for the inspector editor</summary>
        internal Rect HardGuideRect
        {
            get
            {
                Rect r = new Rect(
                        m_Screen.x - m_SoftZoneSize.x / 2, m_Screen.y - m_SoftZoneSize.y / 2,
                        m_SoftZoneSize.x, m_SoftZoneSize.y);
                r.position += new Vector2(
                        m_SoftZoneBias.x * (m_SoftZoneSize.x - m_DeadZone.x),
                        m_SoftZoneBias.y * (m_SoftZoneSize.y - m_DeadZone.y));
                return r;
            }
            set
            {
                m_SoftZoneSize.x = Mathf.Clamp(value.width, 0, 2f);
                m_SoftZoneSize.y = Mathf.Clamp(value.height, 0, 2f);
                m_DeadZone.x = Mathf.Min(m_DeadZone.x, m_SoftZoneSize.x);
                m_DeadZone.y = Mathf.Min(m_DeadZone.y, m_SoftZoneSize.y);

                Vector2 center = value.center;
                Vector2 bias = center - new Vector2(m_Screen.x, m_Screen.y);
                float biasWidth = Mathf.Max(0, m_SoftZoneSize.x - m_DeadZone.x);
                float biasHeight = Mathf.Max(0, m_SoftZoneSize.y - m_DeadZone.y);
                m_SoftZoneBias.x = biasWidth < Epsilon ? 0 : Mathf.Clamp(bias.x / biasWidth, -0.5f, 0.5f);
                m_SoftZoneBias.y = biasHeight < Epsilon ? 0 : Mathf.Clamp(bias.y / biasHeight, -0.5f, 0.5f);
            }
        }

        void OnValidate()
        {
            m_CameraDistance = Mathf.Max(m_CameraDistance, kMinimumCameraDistance);
            m_Damping.x = Mathf.Clamp(m_Damping.x, 0, 20);
            m_Damping.y = Mathf.Clamp(m_Damping.x, 0, 20);
            m_Damping.z = Mathf.Clamp(m_Damping.x, 0, 20);
            m_Screen.x = Mathf.Clamp(m_Screen.x, -0.5f, 1.5f);
            m_Screen.y = Mathf.Clamp(m_Screen.y, -0.5f, 1.5f);
            m_DeadZone.x = Mathf.Clamp(m_DeadZone.x, 0, 2);
            m_DeadZone.y = Mathf.Clamp(m_DeadZone.y, 0, 2);
            m_DeadZone.z = Mathf.Clamp(m_DeadZone.z, 0, 2);
            m_SoftZoneSize.x = Mathf.Clamp(m_SoftZoneSize.x, 0, 2f);
            m_SoftZoneSize.y = Mathf.Clamp(m_SoftZoneSize.y, 0, 2f);
            m_SoftZoneBias.x = Mathf.Clamp(m_SoftZoneBias.x, -0.5f, 0.5f);
            m_SoftZoneBias.y = Mathf.Clamp(m_SoftZoneBias.x, -0.5f, 0.5f);

            m_GroupFramingSize = Mathf.Max(0.001f, m_GroupFramingSize);
            m_MaxDolly.x = Mathf.Max(0, m_MaxDolly.x);
            m_MaxDolly.y = Mathf.Max(0, m_MaxDolly.y);
            m_DistanceRange.x = Mathf.Max(0, m_DistanceRange.x);
            m_DistanceRange.y = Mathf.Max(m_DistanceRange.x, m_DistanceRange.y);
            m_FOVRange.x = Mathf.Max(1, m_FOVRange.x);
            m_FOVRange.y = Mathf.Clamp(m_FOVRange.y, m_FOVRange.x, 179);
            m_OrthoSizeRange.x = Mathf.Max(0.01f, m_OrthoSizeRange.x);
            m_OrthoSizeRange.y = Mathf.Max(m_OrthoSizeRange.x, m_OrthoSizeRange.y);
        }

        /// <summary>True if component is enabled and has a valid Follow target</summary>
        public override bool IsValid { get { return enabled && FollowTarget != null; } }

        /// <summary>Get the Cinemachine Pipeline stage that this component implements.
        /// Always returns the Body stage</summary>
        public override CinemachineCore.Stage Stage { get { return CinemachineCore.Stage.Body; } }

        /// <summary>FramingTransposer's algorithm tahes camera orientation as input, 
        /// so even though it is a Body component, it must apply after Aim</summary>
        public override bool BodyAppliesAfterAim { get { return true; } }

        const float kMinimumCameraDistance = 0.01f;
        const float kMinimumGroupSize = 0.01f;

        /// <summary>State information for damping</summary>
        Vector3 m_PreviousCameraPosition = Vector3.zero;
        internal PositionPredictor m_Predictor = new PositionPredictor();

        /// <summary>Internal API for inspector</summary>
        public Vector3 TrackedPoint { get; private set; }

        /// <summary>This is called to notify the us that a target got warped,
        /// so that we can update its internal state to make the camera
        /// also warp seamlessy.</summary>
        /// <param name="target">The object that was warped</param>
        /// <param name="positionDelta">The amount the target's position changed</param>
        public override void OnTargetObjectWarped(Transform target, Vector3 positionDelta)
        {
            base.OnTargetObjectWarped(target, positionDelta);
            if (target == FollowTarget)
            {
                m_PreviousCameraPosition += positionDelta;
                m_Predictor.ApplyTransformDelta(positionDelta);
            }
        }

        /// <summary>
        /// Force the virtual camera to assume a given position and orientation
        /// </summary>
        /// <param name="pos">Worldspace pposition to take</param>
        /// <param name="rot">Worldspace orientation to take</param>
        public override void ForceCameraPosition(Vector3 pos, Quaternion rot)
        {
            base.ForceCameraPosition(pos, rot);
            m_PreviousCameraPosition = pos;
            m_prevRotation = rot;
        }
        
        /// <summary>
        /// Report maximum damping time needed for this component.
        /// </summary>
        /// <returns>Highest damping setting in this component</returns>
        public override float GetMaxDampTime() 
        { 
            return Mathf.Max(m_Damping.x, Mathf.Max(m_Damping.y, m_Damping.z)); 
        }

        /// <summary>Notification that this virtual camera is going live.
        /// Base class implementation does nothing.</summary>
        /// <param name="fromCam">The camera being deactivated.  May be null.</param>
        /// <param name="worldUp">Default world Up, set by the CinemachineBrain</param>
        /// <param name="deltaTime">Delta time for time-based effects (ignore if less than or equal to 0)</param>
        /// <param name="transitionParams">Transition settings for this vcam</param>
        /// <returns>True if the vcam should do an internal update as a result of this call</returns>
        public override bool OnTransitionFromCamera(
            ICinemachineCamera fromCam, Vector3 worldUp, float deltaTime,
            ref CinemachineVirtualCameraBase.TransitionParams transitionParams)
        {
            if (fromCam != null && transitionParams.m_InheritPosition
                 && !CinemachineCore.Instance.IsLiveInBlend(VirtualCamera))
            {
                m_PreviousCameraPosition = fromCam.State.RawPosition;
                m_prevRotation = fromCam.State.RawOrientation;
                m_InheritingPosition = true;
                return true;
            }
            return false;
        }

        bool m_InheritingPosition;

        // Convert from screen coords to normalized orthographic distance coords
        private Rect ScreenToOrtho(Rect rScreen, float orthoSize, float aspect)
        {
            Rect r = new Rect();
            r.yMax = 2 * orthoSize * ((1f-rScreen.yMin) - 0.5f);
            r.yMin = 2 * orthoSize * ((1f-rScreen.yMax) - 0.5f);
            r.xMin = 2 * orthoSize * aspect * (rScreen.xMin - 0.5f);
            r.xMax = 2 * orthoSize * aspect * (rScreen.xMax - 0.5f);
            return r;
        }

        private Vector3 OrthoOffsetToScreenBounds(Vector3 targetPos2D, Rect screenRect)
        {
            // Bring it to the edge of screenRect, if outside.  Leave it alone if inside.
            Vector3 delta = Vector3.zero;
            if (targetPos2D.x < screenRect.xMin)
                delta.x += targetPos2D.x - screenRect.xMin;
            if (targetPos2D.x > screenRect.xMax)
                delta.x += targetPos2D.x - screenRect.xMax;
            if (targetPos2D.y < screenRect.yMin)
                delta.y += targetPos2D.y - screenRect.yMin;
            if (targetPos2D.y > screenRect.yMax)
                delta.y += targetPos2D.y - screenRect.yMax;
            return delta;
        }

        float m_prevFOV; // State for frame damping
        Quaternion m_prevRotation;

        /// <summary>For editor visulaization of the calculated bounding box of the group</summary>
        public Bounds LastBounds { get; private set; }

        /// <summary>For editor visualization of the calculated bounding box of the group</summary>
        public Matrix4x4 LastBoundsMatrix { get; private set; }

        /// <summary>Positions the virtual camera according to the transposer rules.</summary>
        /// <param name="curState">The current camera state</param>
        /// <param name="deltaTime">Used for damping.  If less than 0, no damping is done.</param>
        public override void MutateCameraState(ref CameraState curState, float deltaTime)
        {
            LensSettings lens = curState.Lens;
            Vector3 followTargetPosition = FollowTargetPosition + (FollowTargetRotation * m_TrackedObjectOffset);
            bool previousStateIsValid = deltaTime >= 0 && VirtualCamera.PreviousStateIsValid;
            if (!previousStateIsValid || VirtualCamera.FollowTargetChanged)
                m_Predictor.Reset();
            if (!previousStateIsValid)
            {
                m_PreviousCameraPosition = curState.RawPosition;
                m_prevFOV = lens.Orthographic ? lens.OrthographicSize : lens.FieldOfView;
                m_prevRotation = curState.RawOrientation;
                if (!m_InheritingPosition && m_CenterOnActivate)
                {
                    m_PreviousCameraPosition = FollowTargetPosition
                        + (curState.RawOrientation * Vector3.back) * m_CameraDistance;
                }
            }
            if (!IsValid)
            {
                m_InheritingPosition = false;
                return;
            }

            var verticalFOV = lens.FieldOfView;

            // Compute group bounds and adjust follow target for group framing
            ICinemachineTargetGroup group = AbstractFollowTargetGroup;
            bool isGroupFraming = group != null && m_GroupFramingMode != FramingMode.None && !group.IsEmpty;
            if (isGroupFraming)
                followTargetPosition = ComputeGroupBounds(group, ref curState);

            TrackedPoint = followTargetPosition;
            if (m_LookaheadTime > Epsilon)
            {
                m_Predictor.Smoothing = m_LookaheadSmoothing;
                m_Predictor.AddPosition(followTargetPosition, deltaTime, m_LookaheadTime);
                var delta = m_Predictor.PredictPositionDelta(m_LookaheadTime);
                if (m_LookaheadIgnoreY)
                    delta = delta.ProjectOntoPlane(curState.ReferenceUp);
                var p = followTargetPosition + delta;
                if (isGroupFraming)
                {
                    var b = LastBounds;
                    b.center += LastBoundsMatrix.MultiplyPoint3x4(delta);
                    LastBounds = b;
                }
                TrackedPoint = p;
            }

            if (!curState.HasLookAt)
                curState.ReferenceLookAt = followTargetPosition;

            // Adjust the desired depth for group framing
            float targetDistance = m_CameraDistance;
            bool isOrthographic = lens.Orthographic;
            float targetHeight = isGroupFraming ? GetTargetHeight(LastBounds.size / m_GroupFramingSize) : 0;
            targetHeight = Mathf.Max(targetHeight, kMinimumGroupSize);
            if (!isOrthographic && isGroupFraming)
            {
                // Adjust height for perspective - we want the height at the near surface
                float boundsDepth = LastBounds.extents.z;
                float z = LastBounds.center.z;
                if (z > boundsDepth)
                    targetHeight = Mathf.Lerp(0, targetHeight, (z - boundsDepth) / z);

                if (m_AdjustmentMode != AdjustmentMode.ZoomOnly)
                {
                    // What distance from near edge would be needed to get the adjusted
                    // target height, at the current FOV
                    targetDistance = targetHeight / (2f * Mathf.Tan(verticalFOV * Mathf.Deg2Rad / 2f));

                    // Clamp to respect min/max distance settings to the near surface of the bounds
                    targetDistance = Mathf.Clamp(targetDistance, m_DistanceRange.x, m_DistanceRange.y);

                    // Clamp to respect min/max camera movement
                    float targetDelta = targetDistance - m_CameraDistance;
                    targetDelta = Mathf.Clamp(targetDelta, -m_MaxDolly.x, m_MaxDolly.y);
                    targetDistance = m_CameraDistance + targetDelta;
                }
            }

            // Optionally allow undamped camera orientation change
            Quaternion localToWorld = curState.RawOrientation;
            if (previousStateIsValid && m_TargetMovementOnly)
            {
                var q = localToWorld * Quaternion.Inverse(m_prevRotation);
                m_PreviousCameraPosition = TrackedPoint + q * (m_PreviousCameraPosition - TrackedPoint);
            }
            m_prevRotation = localToWorld;

            // Work in camera-local space
            Vector3 camPosWorld = m_PreviousCameraPosition;
            Quaternion worldToLocal = Quaternion.Inverse(localToWorld);
            Vector3 cameraPos = worldToLocal * camPosWorld;
            Vector3 targetPos = (worldToLocal * TrackedPoint) - cameraPos;
            Vector3 lookAtPos = targetPos;

            // Move along camera z
            Vector3 cameraOffset = Vector3.zero;
            float cameraMin = Mathf.Max(kMinimumCameraDistance, targetDistance - m_DeadZone.z/2f);
            float cameraMax = Mathf.Max(cameraMin, targetDistance + m_DeadZone.z/2f);
            float targetZ = Mathf.Min(targetPos.z, lookAtPos.z);
            if (targetZ < cameraMin)
                cameraOffset.z = targetZ - cameraMin;
            if (targetZ > cameraMax)
                cameraOffset.z = targetZ - cameraMax;

            // Move along the XY plane
            float screenSize = lens.Orthographic 
                ? lens.OrthographicSize 
                : Mathf.Tan(0.5f * verticalFOV * Mathf.Deg2Rad) * (targetZ - cameraOffset.z);
            Rect softGuideOrtho = ScreenToOrtho(SoftGuideRect, screenSize, lens.Aspect);
            if (!previousStateIsValid)
            {
                // No damping or hard bounds, just snap to central bounds, skipping the soft zone
                Rect rect = softGuideOrtho;
                if (m_CenterOnActivate && !m_InheritingPosition)
                    rect = new Rect(rect.center, Vector2.zero); // Force to center
                cameraOffset += OrthoOffsetToScreenBounds(targetPos, rect);
            }
            else
            {
                // Move it through the soft zone, with damping
                cameraOffset += OrthoOffsetToScreenBounds(targetPos, softGuideOrtho);
                cameraOffset = VirtualCamera.DetachedFollowTargetDamp(
                    cameraOffset, m_Damping, deltaTime);

                // Make sure the real target (not the lookahead one) is still in the frame
                if (!m_UnlimitedSoftZone 
                    && (deltaTime < 0 || VirtualCamera.FollowTargetAttachment > 1 - Epsilon))
                {
                    Rect hardGuideOrtho = ScreenToOrtho(HardGuideRect, screenSize, lens.Aspect);
                    var realTargetPos = (worldToLocal * followTargetPosition) - cameraPos;
                    cameraOffset += OrthoOffsetToScreenBounds(
                        realTargetPos - cameraOffset, hardGuideOrtho);
                }
            }
            curState.RawPosition = localToWorld * (cameraPos + cameraOffset);
            m_PreviousCameraPosition = curState.RawPosition;

            // Adjust lens for group framing
            if (isGroupFraming)
            {
                if (isOrthographic)
                {
                    targetHeight = Mathf.Clamp(targetHeight / 2, m_OrthoSizeRange.x, m_OrthoSizeRange.y);

                    // Apply Damping
                    if (previousStateIsValid)
                        targetHeight = m_prevFOV + VirtualCamera.DetachedFollowTargetDamp(
                            targetHeight - m_prevFOV, m_Damping.z, deltaTime);
                    m_prevFOV = targetHeight;

                    lens.OrthographicSize = Mathf.Clamp(targetHeight, m_OrthoSizeRange.x, m_OrthoSizeRange.y);
                    curState.Lens = lens;
                }
                else if (m_AdjustmentMode != AdjustmentMode.DollyOnly)
                {
                    var localTarget = Quaternion.Inverse(curState.RawOrientation)
                        * (followTargetPosition - curState.RawPosition);
                    float nearBoundsDistance = localTarget.z;
                    float targetFOV = 179;
                    if (nearBoundsDistance > Epsilon)
                        targetFOV = 2f * Mathf.Atan(targetHeight / (2 * nearBoundsDistance)) * Mathf.Rad2Deg;
                    targetFOV = Mathf.Clamp(targetFOV, m_FOVRange.x, m_FOVRange.y);

                    // ApplyDamping
                    if (previousStateIsValid)
                        targetFOV = m_prevFOV + VirtualCamera.DetachedFollowTargetDamp(
                            targetFOV - m_prevFOV, m_Damping.z, deltaTime);
                    m_prevFOV = targetFOV;

                    lens.FieldOfView = targetFOV;
                    curState.Lens = lens;
                }
            }
            m_InheritingPosition = false;
        }

        float GetTargetHeight(Vector2 boundsSize)
        {
            switch (m_GroupFramingMode)
            {
                case FramingMode.Horizontal:
                    return boundsSize.x / VcamState.Lens.Aspect;
                case FramingMode.Vertical:
                    return boundsSize.y;
                default:
                case FramingMode.HorizontalAndVertical:
                    return Mathf.Max(boundsSize.x / VcamState.Lens.Aspect, boundsSize.y);
            }
        }

        Vector3 ComputeGroupBounds(ICinemachineTargetGroup group, ref CameraState curState)
        {
            Vector3 cameraPos = curState.RawPosition;
            Vector3 fwd = curState.RawOrientation * Vector3.forward;

            // Get the bounding box from camera's direction in view space
            LastBoundsMatrix = Matrix4x4.TRS(cameraPos, curState.RawOrientation, Vector3.one);
            Bounds b = group.GetViewSpaceBoundingBox(LastBoundsMatrix);
            Vector3 groupCenter = LastBoundsMatrix.MultiplyPoint3x4(b.center);
            float boundsDepth = b.extents.z;
            if (!curState.Lens.Orthographic)
            {
                // Parallax might change bounds - refine
                float d = (Quaternion.Inverse(curState.RawOrientation) * (groupCenter - cameraPos)).z;
                cameraPos = groupCenter - fwd * (Mathf.Max(d, boundsDepth) + boundsDepth);

                // Will adjust cameraPos
                b = GetScreenSpaceGroupBoundingBox(group, ref cameraPos, curState.RawOrientation);
                LastBoundsMatrix = Matrix4x4.TRS(cameraPos, curState.RawOrientation, Vector3.one);
                groupCenter = LastBoundsMatrix.MultiplyPoint3x4(b.center);
            }
            LastBounds = b;
            return groupCenter - fwd * boundsDepth;
        }

        static Bounds GetScreenSpaceGroupBoundingBox(
            ICinemachineTargetGroup group, ref Vector3 pos, Quaternion orientation)
        {
            var observer = Matrix4x4.TRS(pos, orientation, Vector3.one);
            group.GetViewSpaceAngularBounds(observer, out var minAngles, out var maxAngles, out var zRange);
            var shift = (minAngles + maxAngles) / 2;

            var q = Quaternion.identity.ApplyCameraRotation(new Vector2(-shift.x, shift.y), Vector3.up);
            pos = q * new Vector3(0, 0, (zRange.y + zRange.x)/2);
            pos.z = 0;
            pos = observer.MultiplyPoint3x4(pos);
            observer = Matrix4x4.TRS(pos, orientation, Vector3.one);
            group.GetViewSpaceAngularBounds(observer, out minAngles, out maxAngles, out zRange);

            // For width and height (in camera space) of the bounding box, we use the values at the center of the box.
            // This is an arbitrary choice.  The gizmo drawer will take this into account when displaying
            // the frustum bounds of the group
            var d = zRange.y + zRange.x;
            Vector2 angles = new Vector2(89.5f, 89.5f);
            if (zRange.x > 0)
            {
                angles = Vector2.Max(maxAngles, UnityVectorExtensions.Abs(minAngles));
                angles = Vector2.Min(angles, new Vector2(89.5f, 89.5f));
            }
            angles *= Mathf.Deg2Rad;
            return new Bounds(
                new Vector3(0, 0, d/2),
                new Vector3(Mathf.Tan(angles.y) * d, Mathf.Tan(angles.x) * d, zRange.y - zRange.x));
        }
    }
}

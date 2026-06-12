using UnityEngine;

public class ThirdPersonCamera : MonoBehaviour
{
    [Header("Target")]
    public Transform target; // assign RigidBodyFPController in Inspector

    [Header("Offset")]
    public float distance = 4f;
    public float height = 1.6f;

    [Header("Mouse Settings")]
    public float sensitivity = 3f;
    public float minPitch = -20f;
    public float maxPitch = 60f;

    [Header("Collision")]
    public float collisionRadius = 0.3f;
    public LayerMask collisionLayers;

    private float yaw;
    private float pitch = 10f;

    void Start()
    {
        // If target not assigned in Inspector, try to find it automatically
        if (target == null)
            target = transform.parent;

        yaw = target != null ? target.eulerAngles.y : 0f;
    }

    void LateUpdate()
    {
        if (target == null) return;

        // Read mouse input
        yaw += Input.GetAxis("Mouse X") * sensitivity;
        pitch -= Input.GetAxis("Mouse Y") * sensitivity;
        pitch = Mathf.Clamp(pitch, minPitch, maxPitch);

        // Rotate player body on Y axis only
        target.rotation = Quaternion.Euler(0, yaw, 0);

        // Calculate desired camera position
        Quaternion camRot = Quaternion.Euler(pitch, yaw, 0);
        Vector3 pivotPoint = target.position + Vector3.up * height;
        Vector3 desiredPos = pivotPoint + camRot * new Vector3(0, 0, -distance);

        // Camera collision check
        Vector3 dir = (desiredPos - pivotPoint).normalized;
        float dist = distance;

        if (Physics.SphereCast(pivotPoint, collisionRadius, dir,
            out RaycastHit hit, distance, collisionLayers))
        {
            dist = hit.distance - collisionRadius;
        }

        // Detach from parent transform math ? write world position directly
        transform.position = pivotPoint + camRot * new Vector3(0, 0, -dist);
        transform.rotation = Quaternion.LookRotation(pivotPoint - transform.position);
    }
}
using UnityEngine;
using Unity.Mathematics;
using static CPUDensityManager;

public class RigidFPController : MonoBehaviour
{
    public Camera cam;
    public MouseLook mouseLook = new MouseLook();
    private TerrainCollider tCollider;

    [Header("Movement")]
    public float runSpeed = 14f;
    public float airControl = 0.08f;

    [Header("Jump")]
    public float jumpForce = 10f;
    public float groundStickDist = 0.4f;
    public int coyoteFrames = 8;

    [Header("Gravity")]
    public float gravityAccel = 4f;
    public float maxFallSpeed = 30f;

    [Header("Smoothing")]
    [Range(0f, 1f)]
    public float normalSmoothing = 0.85f;  // higher = smoother but slower to react to real slopes
    [Range(0f, 1f)]
    public float velocitySmoothing = 0.7f; // smooths out jitter in movement direction

    public bool active;
    public bool jumping = false;
    public bool knocked = false;

    private int airFrames = 0;
    private const int CLIFF_GRACE = 12;
    private float currentGravityMult = 1f;

    // Smoothed values ? these absorb bumps and micro-variations
    private float3 smoothedNormal = new float3(0, 1, 0);
    private float3 smoothedVelocity = float3.zero;

    public void Start()
    {
        mouseLook.Init(transform, cam.transform);
        tCollider = GetComponent<TerrainCollider>();
        tCollider.Active = false;
        active = false;
    }

    public void ActivateCharacter()
    {
        tCollider.Active = true;
        active = true;
    }

    public void Update()
    {
        if (!active) return;

        mouseLook.LookRotation(transform, cam.transform);

        Vector2 rawInput = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
        bool hasInput = rawInput.sqrMagnitude > 0.001f;

        float3 posGS = WSToGS(transform.position) + tCollider.offset;
        bool touchingGround = tCollider.SampleCollision(
            posGS,
            new float3(tCollider.size.x, -groundStickDist, tCollider.size.z),
            out float3 rawNormal
        );

        // Smooth the ground normal ? micro-bumps cause tiny normal spikes,
        // smoothing averages them out so movement direction stays stable
        if (touchingGround && math.lengthsq(rawNormal) > 0.001f)
            smoothedNormal = math.normalize(math.lerp(rawNormal, smoothedNormal, normalSmoothing));

        if (touchingGround)
            airFrames = 0;
        else
            airFrames++;

        if (touchingGround && tCollider.velocity.y <= 0)
        {
            jumping = false;
            knocked = false;
            currentGravityMult = 1f;
        }

        if (!touchingGround && !jumping && !knocked && airFrames > CLIFF_GRACE)
        {
            jumping = true;
            airFrames = 0;
        }

        bool grounded = touchingGround && !jumping && !knocked;
        bool canCoyoteJump = !jumping && !knocked && airFrames <= coyoteFrames;

        if (grounded)
        {
            tCollider.useGravity = false;
            tCollider.velocity.y = 0;
            currentGravityMult = 1f;

            if (hasInput)
            {
                float3 flat = (float3)(cam.transform.forward * rawInput.y + cam.transform.right * rawInput.x);
                flat.y = 0;
                if (math.lengthsq(flat) > 0.001f) flat = math.normalize(flat);

                // Use the SMOOTHED normal for slope projection ? not the raw one
                // This prevents a single-frame normal spike from redirecting you sideways
                float3 slopeDir = (float3)Vector3.ProjectOnPlane((Vector3)flat, (Vector3)smoothedNormal);
                if (math.lengthsq(slopeDir) > 0.001f) slopeDir = math.normalize(slopeDir);

                float3 targetVelocity = slopeDir * runSpeed;

                // Smooth velocity toward target ? absorbs single-frame redirections
                // from bump normals without making movement feel sluggish
                smoothedVelocity = math.lerp(targetVelocity, smoothedVelocity, velocitySmoothing);
                tCollider.velocity = new float3(smoothedVelocity.x, 0, smoothedVelocity.z);
            }
            else
            {
                // Instant stop ? clear both
                tCollider.velocity = float3.zero;
                smoothedVelocity = float3.zero;
            }

            if (Input.GetKeyDown(KeyCode.Space))
            {
                tCollider.velocity.y = jumpForce;
                tCollider.useGravity = true;
                jumping = true;
                airFrames = 0;
            }
        }
        else
        {
            if (Input.GetKeyDown(KeyCode.Space) && canCoyoteJump)
            {
                tCollider.velocity.y = jumpForce;
                tCollider.useGravity = true;
                jumping = true;
                airFrames = 0;
                currentGravityMult = 1f;
            }

            tCollider.useGravity = true;

            currentGravityMult = math.min(currentGravityMult + gravityAccel * Time.deltaTime, 10f);
            float3 extraGrav = (float3)Physics.gravity * currentGravityMult * Time.deltaTime;
            tCollider.velocity.y = math.max(tCollider.velocity.y + extraGrav.y, -maxFallSpeed);

            float3 hVel = new float3(tCollider.velocity.x, 0, tCollider.velocity.z);
            if (hasInput)
            {
                float3 flat = (float3)(cam.transform.forward * rawInput.y + cam.transform.right * rawInput.x);
                flat.y = 0;
                if (math.lengthsq(flat) > 0.001f) flat = math.normalize(flat);

                float currentSpeed = math.dot(hVel, flat);
                float addSpeed = math.clamp(runSpeed - currentSpeed, 0, airControl * runSpeed);
                hVel += flat * addSpeed;
            }

            tCollider.velocity = new float3(hVel.x, tCollider.velocity.y, hVel.z);
        }
    }
}
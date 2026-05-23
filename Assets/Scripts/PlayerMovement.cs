using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerMovement : MonoBehaviour
{
    public float speed = 6.0f;
    public float jumpSpeed = 8.0f;
    public float gravity = -9.8f;
    public float jumpHeight = 3f;
    public static bool chunkRebuilding = false;

    public Transform groundCheck;
    public float groundDistance = .4f;
    public LayerMask groundMask;

    private Vector3 velocity;
    private CharacterController controller;
    private bool isGrounded;

    // Start is called before the first frame update
    void Start()
    {
        controller = GetComponent<CharacterController>();
    }

    void Update()
    {
        isGrounded = Physics.CheckSphere(groundCheck.position, groundDistance, groundMask);

        if (Input.GetButtonDown("Jump") && isGrounded)
        {
            velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
        }
        else if (isGrounded && velocity.y < 0)
        {
            velocity.y = -2f;  // only reset when grounded AND not jumping
        }
        else if (!isGrounded)
        {
            velocity.y += gravity * Time.deltaTime;
        }

        float horizontal = Input.GetAxis("Horizontal");
        float vertical = Input.GetAxis("Vertical");
        Vector3 moveDirection = transform.right * horizontal + transform.forward * vertical;
        controller.Move(moveDirection * speed * Time.deltaTime);
        controller.Move(velocity * Time.deltaTime);
    }
}

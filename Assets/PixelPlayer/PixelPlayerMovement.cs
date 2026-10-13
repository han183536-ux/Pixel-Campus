using UnityEngine;

/// <summary>Moves a 2D sprite across a 3D scene's ground plane.</summary>
public class PixelPlayerMovement : MonoBehaviour
{
    [Min(0f)] public float moveSpeed = 4f;

    private Camera mainCamera;
    private WebNotifications.SmartleadWebView school;

    private void Awake()
    {
        mainCamera = Camera.main;
        school = FindFirstObjectByType<WebNotifications.SmartleadWebView>();
    }

    private void Update()
    {
        if (school != null && (!school.IsAuthenticated || school.IsSchoolPageVisible)) return;
        float horizontal = 0f;
        float vertical = 0f;

        if (Input.GetKey(KeyCode.A)) horizontal -= 1f;
        if (Input.GetKey(KeyCode.D)) horizontal += 1f;
        if (Input.GetKey(KeyCode.S)) vertical -= 1f;
        if (Input.GetKey(KeyCode.W)) vertical += 1f;

        Vector3 direction = new Vector3(horizontal, 0f, vertical).normalized;
        transform.position += direction * moveSpeed * Time.deltaTime;
    }

    private void LateUpdate()
    {
        if (mainCamera != null)
            transform.rotation = mainCamera.transform.rotation;
    }
}


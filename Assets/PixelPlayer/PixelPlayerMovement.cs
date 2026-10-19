using UnityEngine;

/// <summary>Moves a 2D sprite across a 3D scene's ground plane.</summary>
public class PixelPlayerMovement : MonoBehaviour
{
    [Min(0f)] public float moveSpeed = 4f;

    private Camera mainCamera;
    private WebNotifications.SmartleadWebView school;
    private WebNotifications.NotificationDetailUI detail;
    private static bool mobileControlsActive, movementPadOpen;
    private GUIStyle movementButton;

    private static Rect MovementToggle
    {
        get { var safe = WebNotifications.CampusScreenLayout.SafeArea; float s = WebNotifications.CampusScreenLayout.Scale;
            return new Rect(safe.xMax - 76 * s, safe.yMax - 42 * s, 68 * s, 34 * s); }
    }
    private static Rect MovementPad
    {
        get { var safe = WebNotifications.CampusScreenLayout.SafeArea; float s = WebNotifications.CampusScreenLayout.Scale;
            return new Rect(safe.xMax - 140 * s, safe.yMax - 182 * s, 132 * s, 132 * s); }
    }
    public static bool BlocksCampusPointer(Vector2 guiPosition) => mobileControlsActive &&
        (MovementToggle.Contains(guiPosition) || movementPadOpen && MovementPad.Contains(guiPosition));

    private void Awake()
    {
        mainCamera = Camera.main;
        school = FindFirstObjectByType<WebNotifications.SmartleadWebView>();
        detail = school != null ? school.GetComponent<WebNotifications.NotificationDetailUI>() : null;
    }

    private void Update()
    {
        bool canMove = (school == null || school.IsAuthenticated && !school.IsSchoolPageVisible) &&
            (detail == null || !detail.IsNotificationPanelOpen);
        mobileControlsActive = Application.isMobilePlatform && canMove;
        if (!canMove) return;
        float horizontal = 0f;
        float vertical = 0f;

        if (Input.GetKey(KeyCode.A)) horizontal -= 1f;
        if (Input.GetKey(KeyCode.D)) horizontal += 1f;
        if (Input.GetKey(KeyCode.S)) vertical -= 1f;
        if (Input.GetKey(KeyCode.W)) vertical += 1f;

        if (mobileControlsActive && movementPadOpen && Input.touchCount == 1)
        {
            var touch = Input.GetTouch(0);
            if (touch.phase != TouchPhase.Ended && touch.phase != TouchPhase.Canceled)
            {
                var position = new Vector2(touch.position.x, Screen.height - touch.position.y);
                if (DirectionRect(1, 0).Contains(position)) vertical += 1;
                if (DirectionRect(1, 2).Contains(position)) vertical -= 1;
                if (DirectionRect(0, 1).Contains(position)) horizontal -= 1;
                if (DirectionRect(2, 1).Contains(position)) horizontal += 1;
            }
        }

        Vector3 direction = new Vector3(horizontal, 0f, vertical).normalized;
        transform.position += direction * moveSpeed * Time.deltaTime;
    }

    private static Rect DirectionRect(int column, int row)
    {
        var pad = MovementPad; float cell = pad.width / 3;
        return new Rect(pad.x + column * cell, pad.y + row * cell, cell, cell);
    }

    private void OnGUI()
    {
        if (!mobileControlsActive) return;
        if (movementButton == null) movementButton = new GUIStyle(GUI.skin.button);
        movementButton.fontSize = Mathf.RoundToInt(13 * WebNotifications.CampusScreenLayout.Scale);
        if (GUI.Button(MovementToggle, movementPadOpen ? "이동 닫기" : "이동", movementButton)) movementPadOpen = !movementPadOpen;
        if (!movementPadOpen) return;
        GUI.Box(MovementPad, GUIContent.none);
        GUI.Box(DirectionRect(1, 0), "▲", movementButton);
        GUI.Box(DirectionRect(1, 2), "▼", movementButton);
        GUI.Box(DirectionRect(0, 1), "◀", movementButton);
        GUI.Box(DirectionRect(2, 1), "▶", movementButton);
    }

    private void OnDisable() { mobileControlsActive = false; movementPadOpen = false; }

    private void LateUpdate()
    {
        if (mainCamera != null)
            transform.rotation = mainCamera.transform.rotation;
    }
}


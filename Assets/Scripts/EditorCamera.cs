using UnityEngine;

public class EditorCamera: MonoBehaviour
{
    [Header("Movement Settings")]
    public float mainSpeed = 10.0f;     // Regular speed
    public float shiftAdd = 25.0f;      // Amount added when holding Shift
    public float mouseSensitivity = 0.25f; 
    
    private float totalRun = 1.0f;
    private Vector3 lastMouse = new Vector3(255, 255, 255); 
    private float camRotationX = 0f;
    private float camRotationY = 0f;

    void Start()
    {
        // Initialize rotation to match current camera orientation
        Vector3 rot = transform.localRotation.eulerAngles;
        camRotationX = rot.y;
        camRotationY = -rot.x;
    }

    void Update()
    {
        // 1. Rotation Logic (Only when Right-Click is held)
        if (Input.GetMouseButton(1))
        {
            lastMouse = Input.mousePosition - lastMouse;
            lastMouse = new Vector3(-lastMouse.y * mouseSensitivity, lastMouse.x * mouseSensitivity, 0);
            lastMouse = new Vector3(transform.eulerAngles.x + lastMouse.x, transform.eulerAngles.y + lastMouse.y, 0);
            transform.eulerAngles = lastMouse;
        }
        lastMouse = Input.mousePosition;

        // 2. Movement Logic
        Vector3 p = GetBaseInput();
        
        if (Input.GetKey(KeyCode.LeftShift))
        {
            totalRun += Time.deltaTime;
            p = p * totalRun * shiftAdd;
            p.x = Mathf.Clamp(p.x, -shiftAdd, shiftAdd);
            p.y = Mathf.Clamp(p.y, -shiftAdd, shiftAdd);
            p.z = Mathf.Clamp(p.z, -shiftAdd, shiftAdd);
        }
        else
        {
            totalRun = Mathf.Clamp(totalRun * 0.5f, 1f, 1000f);
            p = p * mainSpeed;
        }

        p = p * Time.deltaTime;
        transform.Translate(p);
    }

    private Vector3 GetBaseInput()
    { 
        Vector3 p_Velocity = new Vector3();
        
        // Forward/Backward
        if (Input.GetKey(KeyCode.W)) p_Velocity += new Vector3(0, 0, 1);
        if (Input.GetKey(KeyCode.S)) p_Velocity += new Vector3(0, 0, -1);
        
        // Left/Right Strafe
        if (Input.GetKey(KeyCode.A)) p_Velocity += new Vector3(-1, 0, 0);
        if (Input.GetKey(KeyCode.D)) p_Velocity += new Vector3(1, 0, 0);
        
        // Up/Down (Q and E)
        if (Input.GetKey(KeyCode.Q)) p_Velocity += new Vector3(0, -1, 0);
        if (Input.GetKey(KeyCode.E)) p_Velocity += new Vector3(0, 1, 0);

        return p_Velocity;
    }
}
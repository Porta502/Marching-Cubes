using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class PauseMenu : MonoBehaviour
{
    [Header("Panels")]
    public GameObject pausePanel;
    public GameObject settingsPanel;

    [Header("Settings")]
    public Slider sensitivitySlider;
    public Toggle fullscreenToggle;

    [Header("References")]
    public PlayerMovement playerMovement; // your existing player script
    public Camera playerCamera;

    bool isPaused = false;

    // Sensitivity is read by PlayerMovement via this static value
    public static float mouseSensitivity = 2f;

    void Start()
    {
        // Load saved settings
        mouseSensitivity = PlayerPrefs.GetFloat("Sensitivity", 2f);
        sensitivitySlider.value = mouseSensitivity;
        fullscreenToggle.isOn = Screen.fullScreen;

        sensitivitySlider.onValueChanged.AddListener(OnSensitivityChanged);
        fullscreenToggle.onValueChanged.AddListener(OnFullscreenChanged);

        pausePanel.SetActive(false);
        settingsPanel.SetActive(false);
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (settingsPanel.activeSelf)
                CloseSettings();
            else if (isPaused)
                Resume();
            else
                Pause();
        }
    }

    public void Resume()
    {
        pausePanel.SetActive(false);
        settingsPanel.SetActive(false);
        Time.timeScale = 1f;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        isPaused = false;
    }

    void Pause()
    {
        pausePanel.SetActive(true);
        Time.timeScale = 0f;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        isPaused = true;
    }

    public void OpenSettings()
    {
        pausePanel.SetActive(false);
        settingsPanel.SetActive(true);
    }

    public void CloseSettings()
    {
        settingsPanel.SetActive(false);
        pausePanel.SetActive(true);
    }

    void OnSensitivityChanged(float value)
    {
        mouseSensitivity = value;
        PlayerPrefs.SetFloat("Sensitivity", value);
    }

    void OnFullscreenChanged(bool value)
    {
        Screen.fullScreen = value;
    }

    public void ExitGame()
    {
        Time.timeScale = 1f;
        Application.Quit();

        // In Editor this won't quit, so stop play mode instead
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#endif
    }
}
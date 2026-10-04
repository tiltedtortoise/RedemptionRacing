using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Escape pauses the race (Time.timeScale = 0, audio paused) and shows Resume / Restart / Exit.
/// All race systems run on scaled time, so they freeze while paused. Not available once the race has finished.
/// </summary>
[DisallowMultipleComponent]
public class PauseMenu : MonoBehaviour
{
    [SerializeField] private RaceManager race;
    [Tooltip("Restart and Exit reuse the result screen's behaviour.")]
    [SerializeField] private RaceHUD hud;
    [SerializeField] private GameObject pauseRoot;
    [SerializeField] private Button resumeButton;
    [SerializeField] private Button restartButton;
    [SerializeField] private Button exitButton;

    public static bool IsPaused { get; private set; }

    private void Awake()
    {
        resumeButton.onClick.AddListener(Resume);
        restartButton.onClick.AddListener(RestartRace);
        exitButton.onClick.AddListener(ExitGame);
        pauseRoot.SetActive(false);
    }

    private void Update()
    {
        bool pressed = false;
#if ENABLE_INPUT_SYSTEM
        pressed = Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER
        pressed = Input.GetKeyDown(KeyCode.Escape);
#endif
        if (!pressed) return;
        if (IsPaused) Resume();
        else if (race != null && !race.IsFinished) Pause(); // The result screen keeps its own Restart/Exit flow.
    }

    public void Pause()
    {
        IsPaused = true;
        Time.timeScale = 0f;
        AudioListener.pause = true;
        pauseRoot.SetActive(true);
        resumeButton.Select();
    }

    public void Resume()
    {
        IsPaused = false;
        Time.timeScale = 1f;
        AudioListener.pause = false;
        pauseRoot.SetActive(false);
    }

    private void RestartRace()
    {
        Resume();
        hud.Restart();
    }

    private void ExitGame()
    {
        Resume();
        hud.Exit();
    }

    private void OnDestroy()
    {
        // Never leave the next scene or session paused (the panel itself may already be gone).
        if (IsPaused)
        {
            IsPaused = false;
            Time.timeScale = 1f;
            AudioListener.pause = false;
        }
        resumeButton.onClick.RemoveListener(Resume);
        restartButton.onClick.RemoveListener(RestartRace);
        exitButton.onClick.RemoveListener(ExitGame);
    }
}

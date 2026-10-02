using System;
using System.Globalization;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class RaceHUD : MonoBehaviour
{
    [SerializeField] private RaceManager race;
    [SerializeField] private CarController car;
    [SerializeField] private GameObject hudRoot;
    [SerializeField] private GameObject resultsRoot;
    [SerializeField] private TMP_Text lapText;
    [SerializeField] private TMP_Text positionText;
    [SerializeField] private TMP_Text speedText;
    [SerializeField] private TMP_Text timingText;
    [SerializeField] private TMP_Text resultsText;
    [SerializeField] private Button restartButton;
    [SerializeField] private Button exitButton;
    [SerializeField, Min(0f)] private float resultsFadeSeconds = 0.6f;

    [Header("Best Lap Highlight")]
    [SerializeField] private Color bestLapHighlightColor = new Color(1f, 0.82f, 0.3f);
    [SerializeField, Min(0f)] private float bestLapHighlightSeconds = 2f;

    [Header("Ghost Split (optional)")]
    [SerializeField] private TMP_Text splitText;
    [SerializeField, Min(0f)] private float splitHoldSeconds = 1.4f;
    [SerializeField, Min(0.01f)] private float splitFadeSeconds = 0.4f;
    [SerializeField] private Color splitAheadColor = new Color(0.3f, 1f, 0.48f);
    [SerializeField] private Color splitBehindColor = new Color(1f, 0.35f, 0.35f);

    private CanvasGroup resultsGroup;
    private int seenLapCount;
    private float bestLapHighlightUntil = -1f;
    private float splitShownAt = float.NegativeInfinity;

    private void Awake()
    {
        restartButton.onClick.AddListener(Restart);
        exitButton.onClick.AddListener(Exit);
        resultsGroup = resultsRoot.GetComponent<CanvasGroup>();
        if (resultsGroup == null) resultsGroup = resultsRoot.AddComponent<CanvasGroup>();
        if (splitText != null) splitText.enabled = false;
        Refresh();
    }

    private void OnEnable() { if (race != null) race.CheckpointPassed += ShowSplit; }
    private void OnDisable() { if (race != null) race.CheckpointPassed -= ShowSplit; }

    private void Update()
    {
        Refresh();
        if (splitText != null && splitText.enabled)
        {
            float alpha = 1f - (Time.time - splitShownAt - splitHoldSeconds) / splitFadeSeconds;
            splitText.alpha = Mathf.Clamp01(alpha);
            if (alpha <= 0f) splitText.enabled = false;
        }
    }

    /// <summary>Player time minus the ghost's recorded time at the same pass (same checkpoint, same lap).</summary>
    private void ShowSplit(int passOrdinal, float playerRaceTime)
    {
        if (splitText == null || passOrdinal == 0) return; // Pass 0 is just leaving the grid.
        var ghost = race.Opponent;
        if (ghost == null || !ghost.IsAvailable || !ghost.TryGetSplitReference(passOrdinal, out float ghostRaceTime)) return;
        float delta = playerRaceTime - ghostRaceTime;
        splitText.text = (delta < 0f ? "-" : "+") + Mathf.Abs(delta).ToString("0.000", CultureInfo.InvariantCulture);
        splitText.color = delta < 0f ? splitAheadColor : splitBehindColor;
        splitText.alpha = 1f;
        splitText.enabled = true;
        splitShownAt = Time.time;
    }

    public void Refresh()
    {
        if (race == null || car == null) return;
        bool showResults = race.IsFinished;
        bool justFinished = showResults && !resultsRoot.activeSelf;
        hudRoot.SetActive(race.IsRacing);
        resultsRoot.SetActive(showResults);
        string best = race.CompletedLapTimes.Count > 0 ? FormatTime(race.BestLapTime) : "--:--.---";
        if (race.IsRacing)
        {
            if (positionText != null) positionText.text = race.Opponent != null && race.Opponent.IsAvailable ? $"P{race.PlayerPosition} / 2" : "";
            lapText.text = $"Lap {race.CurrentLap} / {race.TotalLaps}";
            speedText.text = $"{Mathf.RoundToInt(car.CurrentSpeedKmh)} km/h";
            timingText.text = $"Lap: {FormatTime(race.CurrentLapTime)}\nTotal: {FormatTime(race.TotalRaceTime)}\n{BestLapLine(best)}";
        }
        if (showResults)
        {
            if (justFinished)
            {
                resultsGroup.alpha = 0f;
                resultsText.text = BuildResults(best);
                restartButton.Select();
            }
            // Unscaled so the fade also works if time is ever paused on the results screen.
            resultsGroup.alpha = resultsFadeSeconds <= 0f ? 1f
                : Mathf.MoveTowards(resultsGroup.alpha, 1f, Time.unscaledDeltaTime / resultsFadeSeconds);
        }
    }

    /// <summary>Flashes the Best line when a completed lap sets a new best, then fades back to the normal text colour.</summary>
    private string BestLapLine(string best)
    {
        var laps = race.CompletedLapTimes;
        if (laps.Count > seenLapCount)
        {
            seenLapCount = laps.Count;
            if (laps[laps.Count - 1] == race.BestLapTime)
                bestLapHighlightUntil = Time.time + bestLapHighlightSeconds;
        }
        string line = $"Best: {best}";
        float remaining = bestLapHighlightUntil - Time.time;
        if (remaining <= 0f || bestLapHighlightSeconds <= 0f) return line;
        // Hold the highlight for the first half, then blend back to the normal colour.
        float blend = Mathf.Clamp01(remaining / (bestLapHighlightSeconds * 0.5f));
        Color color = Color.Lerp(timingText.color, bestLapHighlightColor, blend);
        return $"<color=#{ColorUtility.ToHtmlStringRGBA(color)}>{line}</color>";
    }

    private string BuildResults(string best)
    {
        string title = !race.HasOpponent ? "<color=#FFFFFF>FINISH</color>"
            : race.PlayerWon ? "<color=#4CFF7A>VICTORY</color>" : "<color=#FF5A5A>DEFEAT</color>";
        var text = new StringBuilder();
        text.Append($"<size=160%><b>{title}</b></size>\n");
        text.Append($"Position: P{race.PlayerPosition} / {race.RacerCount}\n");
        text.Append($"Total Time: {FormatTime(race.TotalRaceTime)}\n");
        var laps = race.CompletedLapTimes;
        if (laps.Count == 0) text.Append("<color=#AAAAAA>No completed laps</color>\n");
        for (int i = 0; i < laps.Count; i++)
        {
            string line = $"Lap {i + 1}: {FormatTime(laps[i])}";
            text.Append(laps[i] == race.BestLapTime ? $"<color=#FFD24C>{line}</color>\n" : line + "\n");
        }
        text.Append($"Best Lap: {best}");
        return text.ToString();
    }

    public static string FormatTime(float seconds)
    {
        if (float.IsNaN(seconds) || float.IsInfinity(seconds)) return "--:--.---";
        long milliseconds = (long)Math.Round(Math.Max(0d, seconds) * 1000d, MidpointRounding.AwayFromZero);
        return string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}.{2:000}",
            milliseconds / 60000, milliseconds / 1000 % 60, milliseconds % 1000);
    }

    public void Restart()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().path);
    }

    public void Exit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private void OnDestroy()
    {
        if (restartButton != null) restartButton.onClick.RemoveListener(Restart);
        if (exitButton != null) exitButton.onClick.RemoveListener(Exit);
    }
}

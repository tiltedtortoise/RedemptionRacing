using System;
using System.Globalization;
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

    private void Awake()
    {
        restartButton.onClick.AddListener(Restart);
        exitButton.onClick.AddListener(Exit);
        Refresh();
    }

    private void Update() => Refresh();

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
            timingText.text = $"Lap: {FormatTime(race.CurrentLapTime)}\nTotal: {FormatTime(race.TotalRaceTime)}\nBest: {best}";
        }
        if (showResults)
        {
            resultsText.text = $"FINISH\n\nTotal Time: {FormatTime(race.TotalRaceTime)}\nBest Lap: {best}\nLast Lap: {FormatTime(race.LastLapTime)}";
            if (justFinished) restartButton.Select();
        }
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

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GameManager : SingletonAutoMono<GameManager>
{
    public enum GameState
    {
        Lobby,
        InGame,
        Paused,
        GameOver
    }

    private GameState currentState = GameState.Lobby;

    public GameState CurrentState
    {
        get { return currentState; }
    }

    public void SetGameState(GameState newState)
    {
        currentState = newState;
        // 可以在这里添加状态变化时的逻辑处理
        Debug.Log("Game State changed to: " + newState.ToString());
    }
    public void ResetGame()
    {
        // 重置游戏逻辑
        SetGameState(GameState.Lobby);
        Debug.Log("Game has been reset to Lobby state.");
    }
    public void StartGame()
    {
        SetGameState(GameState.InGame);
        Debug.Log("Game has started.");
    }
    public void PauseGame()
    {
        SetGameState(GameState.Paused);
        Debug.Log("Game is paused.");
    }
    public void EndGame()
    {
        SetGameState(GameState.GameOver);
        Debug.Log("Game Over.");
    }
    public void getCurrentState()
    {
        Debug.Log("Current Game State: " + currentState.ToString());
    }
}

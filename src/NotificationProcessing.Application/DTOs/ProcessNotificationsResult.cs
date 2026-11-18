namespace NotificationProcessing.Application.DTOs;

/// <param name="Selected">Due notifications read in this run (at most the batch size).</param>
/// <param name="Sent">Delivered successfully.</param>
/// <param name="Failed">Provider failed; status set to Failed with a reason.</param>
/// <param name="Skipped">Already claimed by another concurrent run, so not touched by this one.</param>
public sealed record ProcessNotificationsResult(int Selected, int Sent, int Failed, int Skipped);

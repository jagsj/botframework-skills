﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Threading;
using System.Threading.Tasks;
using Luis;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Schema;
using Microsoft.Bot.Solutions;
using Microsoft.Bot.Solutions.Responses;
using Microsoft.Extensions.DependencyInjection;
using MusicSkill.Models;
using MusicSkill.Models.ActionInfos;
using MusicSkill.Responses.Main;
using MusicSkill.Responses.Shared;
using MusicSkill.Services;
using Newtonsoft.Json.Linq;
using SkillServiceLibrary.Utilities;

namespace MusicSkill.Dialogs
{
    public class MainDialog : ComponentDialog
    {
        private readonly BotSettings _settings;
        private readonly BotServices _services;
        private readonly LocaleTemplateManager _templateManager;
        private readonly IStatePropertyAccessor<SkillState> _stateAccessor;
        private readonly Dialog _playMusicDialog;
        private readonly Dialog _controlSettingsDialog;

        public MainDialog(
            IServiceProvider serviceProvider)
            : base(nameof(MainDialog))
        {
            _settings = serviceProvider.GetService<BotSettings>();
            _services = serviceProvider.GetService<BotServices>();
            _templateManager = serviceProvider.GetService<LocaleTemplateManager>();

            // Create conversation state properties
            var conversationState = serviceProvider.GetService<ConversationState>();
            _stateAccessor = conversationState.CreateProperty<SkillState>(nameof(SkillState));

            var steps = new WaterfallStep[]
            {
                IntroStepAsync,
                RouteStepAsync,
                FinalStepAsync,
            };

            AddDialog(new WaterfallDialog(nameof(MainDialog), steps));
            AddDialog(new TextPrompt(nameof(TextPrompt)));
            InitialDialogId = nameof(MainDialog);

            // Register dialogs
            _playMusicDialog = serviceProvider.GetService<PlayMusicDialog>() ?? throw new ArgumentNullException(nameof(PlayMusicDialog));
            _controlSettingsDialog = serviceProvider.GetService<ControlSettingsDialog>() ?? throw new ArgumentNullException(nameof(ControlSettingsDialog));
            AddDialog(_playMusicDialog);
            AddDialog(_controlSettingsDialog);
        }

        // Runs when the dialog is started.
        protected override async Task<DialogTurnResult> OnBeginDialogAsync(DialogContext innerDc, object options, CancellationToken cancellationToken = default)
        {
            if (innerDc.Context.Activity.Type == ActivityTypes.Message && !string.IsNullOrEmpty(innerDc.Context.Activity.Text))
            {
                // Get cognitive models for the current locale.
                var localizedServices = _services.GetCognitiveModels();

                    // Run LUIS recognition on Skill model and store result in turn state.
                localizedServices.LuisServices.TryGetValue("MusicSkill", out var skillLuisService);
                if (skillLuisService != null)
                {
                    var skillResult = await skillLuisService.RecognizeAsync<MusicSkillLuis>(innerDc.Context, cancellationToken);
                    innerDc.Context.TurnState[StateProperties.MusicLuisResultKey] = skillResult;
                }
                else
                {
                    throw new Exception("The skill LUIS Model could not be found in your Bot Services configuration.");
                }

                // Run LUIS recognition on General model and store result in turn state.
                localizedServices.LuisServices.TryGetValue("General", out var generalLuisService);
                if (generalLuisService != null)
                {
                    var generalResult = await generalLuisService.RecognizeAsync<GeneralLuis>(innerDc.Context, cancellationToken);
                    innerDc.Context.TurnState[StateProperties.GeneralLuisResultKey] = generalResult;
                }
                else
                {
                    throw new Exception("The general LUIS Model could not be found in your Bot Services configuration.");
                }

                // Check for any interruptions
                var interrupted = await InterruptDialogAsync(innerDc, cancellationToken);

                if (interrupted != null)
                {
                    // If dialog was interrupted, return interrupted result
                    return interrupted;
                }
            }

            return await base.OnBeginDialogAsync(innerDc, options, cancellationToken);
        }

        // Runs on every turn of the conversation.
        protected override async Task<DialogTurnResult> OnContinueDialogAsync(DialogContext innerDc, CancellationToken cancellationToken = default)
        {
            if (innerDc.Context.Activity.Type == ActivityTypes.Message && !string.IsNullOrEmpty(innerDc.Context.Activity.Text))
            {
                // Get cognitive models for the current locale.
                var localizedServices = _services.GetCognitiveModels();

                // Run LUIS recognition on Skill model and store result in turn state.
                localizedServices.LuisServices.TryGetValue("MusicSkill", out var skillLuisService);
                if (skillLuisService != null)
                {
                    var skillResult = await skillLuisService.RecognizeAsync<MusicSkillLuis>(innerDc.Context, cancellationToken);
                    innerDc.Context.TurnState[StateProperties.MusicLuisResultKey] = skillResult;
                }
                else
                {
                    throw new Exception("The skill LUIS Model could not be found in your Bot Services configuration.");
                }

                // Run LUIS recognition on General model and store result in turn state.
                localizedServices.LuisServices.TryGetValue("General", out var generalLuisService);
                if (generalLuisService != null)
                {
                    var generalResult = await generalLuisService.RecognizeAsync<GeneralLuis>(innerDc.Context, cancellationToken);
                    innerDc.Context.TurnState[StateProperties.GeneralLuisResultKey] = generalResult;
                }
                else
                {
                    throw new Exception("The general LUIS Model could not be found in your Bot Services configuration.");
                }

                // Check for any interruptions
                var interrupted = await InterruptDialogAsync(innerDc, cancellationToken);

                if (interrupted != null)
                {
                    // If dialog was interrupted, return interrupted result
                    return interrupted;
                }
            }

            return await base.OnContinueDialogAsync(innerDc, cancellationToken);
        }

        // Runs on every turn of the conversation to check if the conversation should be interrupted.
        protected async Task<DialogTurnResult> InterruptDialogAsync(DialogContext innerDc, CancellationToken cancellationToken)
        {
            DialogTurnResult interrupted = null;
            var activity = innerDc.Context.Activity;

            if (activity.Type == ActivityTypes.Message && !string.IsNullOrEmpty(activity.Text))
            {
                // Get connected LUIS result from turn state.
                var state = await _stateAccessor.GetAsync(innerDc.Context, () => new SkillState());
                var generalResult = innerDc.Context.TurnState.Get<GeneralLuis>(StateProperties.GeneralLuisResultKey);
                (var generalIntent, var generalScore) = generalResult.TopIntent();

                if (generalScore > 0.5)
                {
                    switch (generalIntent)
                    {
                        case GeneralLuis.Intent.Cancel:
                            {
                                await innerDc.Context.SendActivityAsync(_templateManager.GenerateActivityForLocale(MainResponses.CancelMessage), cancellationToken);
                                await innerDc.CancelAllDialogsAsync();
                                if (innerDc.Context.IsSkill())
                                {
                                    interrupted = await innerDc.EndDialogAsync(state.IsAction ? new ActionResult() { ActionSuccess = false } : null, cancellationToken: cancellationToken);
                                }
                                else
                                {
                                    interrupted = await innerDc.BeginDialogAsync(InitialDialogId, cancellationToken: cancellationToken);
                                }

                                break;
                            }

                        case GeneralLuis.Intent.Help:
                            {
                                await innerDc.Context.SendActivityAsync(_templateManager.GenerateActivityForLocale(MainResponses.HelpMessage), cancellationToken);
                                await innerDc.RepromptDialogAsync(cancellationToken);
                                interrupted = EndOfTurn;
                                break;
                            }

                        case GeneralLuis.Intent.Logout:
                            {
                                await LogUserOutAsync(innerDc, cancellationToken);

                                await innerDc.Context.SendActivityAsync(_templateManager.GenerateActivityForLocale(MainResponses.LogOut), cancellationToken);
                                await innerDc.CancelAllDialogsAsync(cancellationToken);
                                if (innerDc.Context.IsSkill())
                                {
                                    interrupted = await innerDc.EndDialogAsync(state.IsAction ? new ActionResult() { ActionSuccess = false } : null, cancellationToken: cancellationToken);
                                }
                                else
                                {
                                    interrupted = await innerDc.BeginDialogAsync(InitialDialogId, cancellationToken: cancellationToken);
                                }

                                break;
                            }
                    }
                }
            }

            return interrupted;
        }

        // Handles introduction/continuation prompt logic.
        private async Task<DialogTurnResult> IntroStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            if (stepContext.Context.IsSkill())
            {
                // If the bot is in skill mode, skip directly to route and do not prompt
                return await stepContext.NextAsync(cancellationToken: cancellationToken);
            }
            else
            {
                // If bot is in local mode, prompt with intro or continuation message
                var promptOptions = new PromptOptions
                {
                    Prompt = stepContext.Options as Activity ?? _templateManager.GenerateActivityForLocale(MainResponses.FirstPromptMessage)
                };

                if (stepContext.Context.Activity.Type == ActivityTypes.ConversationUpdate)
                {
                    promptOptions.Prompt = _templateManager.GenerateActivityForLocale(MainResponses.WelcomeMessage);
                }

                return await stepContext.PromptAsync(nameof(TextPrompt), promptOptions, cancellationToken);
            }
        }

        // Handles routing to additional dialogs logic.
        private async Task<DialogTurnResult> RouteStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var a = stepContext.Context.Activity;
            var state = await _stateAccessor.GetAsync(stepContext.Context, () => new SkillState(), cancellationToken);
            state.Clear();
            if (a.Type == ActivityTypes.Message && !string.IsNullOrEmpty(a.Text))
            {
                var skillResult = stepContext.Context.TurnState.Get<MusicSkillLuis>(StateProperties.MusicLuisResultKey);
                var intent = skillResult?.TopIntent().intent;

                // switch on general intents
                switch (intent)
                {
                    case MusicSkillLuis.Intent.PlayMusic:
                        {
                            return await stepContext.BeginDialogAsync(_playMusicDialog.Id, cancellationToken: cancellationToken);
                        }

                    case MusicSkillLuis.Intent.Exclude:
                        {
                            state.ControlActionName = ControlActions.Exclude;
                            return await stepContext.BeginDialogAsync(_controlSettingsDialog.Id, cancellationToken: cancellationToken);
                        }

                    case MusicSkillLuis.Intent.Pause:
                        {
                            state.ControlActionName = ControlActions.Pause;
                            return await stepContext.BeginDialogAsync(_controlSettingsDialog.Id, cancellationToken: cancellationToken);
                        }

                    case MusicSkillLuis.Intent.Shuffle:
                        {
                            state.ControlActionName = ControlActions.Shuffle;
                            return await stepContext.BeginDialogAsync(_controlSettingsDialog.Id, cancellationToken: cancellationToken);
                        }

                    case MusicSkillLuis.Intent.AdjustVolume:
                        {
                            state.ControlActionName = ControlActions.AdjustVolume;
                            return await stepContext.BeginDialogAsync(_controlSettingsDialog.Id, cancellationToken: cancellationToken);
                        }

                    case MusicSkillLuis.Intent.None:
                        {
                            // No intent was identified, send confused message
                            await stepContext.Context.SendActivityAsync(_templateManager.GenerateActivityForLocale(SharedResponses.DidntUnderstandMessage), cancellationToken);
                            break;
                        }

                    case MusicSkillLuis.Intent.AnswerMusicQuestion:
                    default:
                        {
                            // intent was identified but not yet implemented
                            await stepContext.Context.SendActivityAsync(_templateManager.GenerateActivityForLocale(MainResponses.FeatureNotAvailable), cancellationToken);
                            break;
                        }
                }
            }
            else if (a.Type == ActivityTypes.Event)
            {
                var ev = a.AsEventActivity();

                switch (ev.Name)
                {
                    case TokenEvents.TokenResponseEventName:
                        {
                            // Auth dialog completion
                            var result = await stepContext.ContinueDialogAsync(cancellationToken);

                            // If the dialog completed when we sent the token, end the skill conversation
                            if (result.Status != DialogTurnStatus.Waiting)
                            {
                                var response = stepContext.Context.Activity.CreateReply();
                                response.Type = ActivityTypes.EndOfConversation;

                                await stepContext.Context.SendActivityAsync(response, cancellationToken);
                            }

                            break;
                        }

                    case Events.PlayMusic:
                        {
                            state.IsAction = true;
                            SearchInfo actionData = null;
                            if (ev.Value is JObject info)
                            {
                                actionData = info.ToObject<SearchInfo>();
                                actionData.DigestState(state);
                            }

                            return await stepContext.BeginDialogAsync(_playMusicDialog.Id, cancellationToken: cancellationToken);
                        }

                    default:
                        {
                            await stepContext.Context.SendActivityAsync(new Activity(type: ActivityTypes.Trace, text: $"Unknown Event '{ev.Name ?? "undefined"}' was received but not processed."), cancellationToken);
                            break;
                        }
                }
            }

            // If activity was unhandled, flow should continue to next step
            return await stepContext.NextAsync(cancellationToken: cancellationToken);
        }

        // Handles conversation cleanup.
        private async Task<DialogTurnResult> FinalStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var state = await _stateAccessor.GetAsync(stepContext.Context, () => new SkillState(), cancellationToken);
            if (stepContext.Context.IsSkill())
            {
                var result = stepContext.Result;

                if (state.IsAction && result as ActionResult == null)
                {
                    result = new ActionResult() { ActionSuccess = false };
                }

                state.Clear();
                return await stepContext.EndDialogAsync(result, cancellationToken);
            }
            else
            {
                state.Clear();
                return await stepContext.ReplaceDialogAsync(InitialDialogId, _templateManager.GenerateActivityForLocale(MainResponses.CompletedMessage), cancellationToken);
            }
        }

        private async Task LogUserOutAsync(DialogContext dc, CancellationToken cancellationToken)
        {
            var supported = dc.Context.Adapter is IUserTokenProvider;
            if (supported)
            {
                var tokenProvider = (IUserTokenProvider)dc.Context.Adapter;

                // Sign out user
                var tokens = await tokenProvider.GetTokenStatusAsync(dc.Context, dc.Context.Activity.From.Id, cancellationToken: cancellationToken);
                foreach (var token in tokens)
                {
                    await tokenProvider.SignOutUserAsync(dc.Context, token.ConnectionName, cancellationToken: cancellationToken);
                }

                // Cancel all active dialogs
                await dc.CancelAllDialogsAsync(cancellationToken);
            }
            else
            {
                throw new InvalidOperationException("OAuthPrompt.SignOutUser(): not supported by the current adapter");
            }
        }

        private static class Events
        {
            public const string PlayMusic = "PlayMusic";
        }
    }
}
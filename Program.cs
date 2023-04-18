// This example is based on the sample code in "How to recognize speech" in
//     https://learn.microsoft.com/en-us/azure/cognitive-services/speech-service/how-to-recognize-speech?pivots=programming-language-csharp

using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using Microsoft.CognitiveServices.Speech.Translation;
using System.Text;

class Program
{
    static int Main(string[] args)
    {
        const string profanityOption = "Masked";

        // Parsing the command-line options.
        if (args.Length < 2)
        {
            Console.WriteLine("======================================================================================");
            Console.WriteLine("Please provide the *.wav filename and the language/locality for the expected language,");
            Console.WriteLine("    For instance on speech-to-text services, CsharpAzureSpeechCaption.exe sample.wav en-US");
            Console.WriteLine("    or on translation services, CsharpAzureSpeechCaption.exe sample.wav en-US en,zh-Hant");
            return 1;
        }

        var speechSourceFile = args[0];
        // Check if speechSourceFile is valid.
        if (!File.Exists(speechSourceFile))
        {
            Console.WriteLine("======================================================================");
            Console.WriteLine("The provided speechSourceFile " + speechSourceFile + " does not exist!");
            return 1;
        }
        else
        {
            FileInfo fi = new FileInfo(speechSourceFile);
            string fiExtension = fi.Extension;
            if (!fiExtension.Equals(".wav", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("===========================================================================================");
                Console.WriteLine("The provided speechSourceFile " + speechSourceFile + " is not an expected audio *.wav file!");
                return 1;
            }
        }

        var sourceLanguage = args[1];
        string[] listTargetLanguages = new string[0];
        if (args.Length == 3)
        {
            // We are specifying target language(s), which means we need translation.  args[2] is a comma-separated list of
            // target languages.
            listTargetLanguages = args[2].Split(',');
        }

        // Parsing the Azure credentials via Windows environmental variables.
        var speechKey = Environment.GetEnvironmentVariable("SPEECH_KEY");
        var speechRegion = Environment.GetEnvironmentVariable("SPEECH_REGION");
        if (speechKey is null || speechRegion is null)
        {
            Console.WriteLine("=======================================================================================");
            Console.WriteLine("Please set the environmental variables SPEECH_KEY and SPEECH_REGION for proper billing.");
            return 2;
        }

        SpeechConfig speechConfig;
        if (listTargetLanguages.Length == 0)
        {
            speechConfig = SpeechConfig.FromSubscription(speechKey, speechRegion);
        }
        else
        {
            speechConfig = SpeechTranslationConfig.FromSubscription(speechKey, speechRegion);
        }
        speechConfig.SetProperty(PropertyId.SpeechServiceResponse_ProfanityOption, profanityOption);
        speechConfig.OutputFormat = OutputFormat.Detailed;
        // For Azure speech to text, the source language is also the target language (in text).
        speechConfig.SpeechRecognitionLanguage = sourceLanguage;
        foreach (string targetLanguage in listTargetLanguages)
        {
            ((SpeechTranslationConfig) speechConfig).AddTargetLanguage(targetLanguage);
        }

        // Enabling Speech SDK logging for translation only, tentatlively.
        if (listTargetLanguages.Length > 0)
        {
            var speechSDKLogPath = System.Environment.GetEnvironmentVariable("TEMP") ?? Directory.GetCurrentDirectory();
            string speechSDKLogFile = speechSDKLogPath + "\\speechSDKlog.txt";
            Console.WriteLine($"=== SDK Log File Path: " + speechSDKLogFile);
            speechConfig.SetProperty(PropertyId.Speech_LogFilename, speechSDKLogFile);
        }

        Console.OutputEncoding = Encoding.Unicode; // For UTF-16; this is for displaying some fonts like Japanese.

        RecognitionFromWav(speechConfig, speechSourceFile, listTargetLanguages).GetAwaiter().GetResult();

        return 0;
    }

    async static Task RecognitionFromWav(SpeechConfig speechConfig, string speechSourceFile, string[] listTargetLangs)
    {
        var stopRecognition = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

        using (var audioInput = AudioConfig.FromWavFileInput(speechSourceFile))
        {
            if (listTargetLangs.Length == 0)
            {
                // Azure speech to text mode.
                var speechRecognizer = new SpeechRecognizer(speechConfig, audioInput);

                // Subscribing to the miscellaneous events.
                speechRecognizer.Recognizing += (s, e) =>
                {
                    // Recognizing events give a partial result (as compared to Recognized events)
                    Console.WriteLine($"      RECOGNIZING: Text= {e.Result.Text}");
                };

                speechRecognizer.Recognized += (s, e) =>
                {
                    if (e.Result.Reason == ResultReason.RecognizedSpeech)
                    {
                        Console.WriteLine($"RECOGNIZED: Text= {e.Result.Text}");
                    }
                    else
                    {
                        Console.WriteLine($"RECOGNIZED UNLISTED REASON: Text={e.Result.Reason}.");
                    }
                    Console.WriteLine("");
                };

                speechRecognizer.Canceled += (s, e) =>
                {
                    Console.WriteLine($"CANCELED: Reason={e.Reason}");

                    // * If the specified region is invalid, we have
                    //       ErrorCode = ConnectionFailure
                    //       ErrorDetails = Connection failed (no connection to the remote host). Internal error: 1.
                    //                      Error details: Failed with error: WS_OPEN_ERROR_UNDERLYING_IO_OPEN_FAILED
                    // * If the specified speech key is invalid, we have
                    //       ErrorCode = AuthenticationFailure
                    //       ErrorDetails = WebSocket upgrade failed: Authentication error (401). Please check subscription information and region name.
                    // * If the specified language is invalid (say, "en-JP"), we have
                    //       ErrorCode = BadRequest
                    //       ErrorDetails = WebSocket upgrade failed: Bad request (400). Error details: Failed with HTTP 400 Bad Request ......
                    // The following errors are observed though we do not yet figure out the reasons.
                    // * ErrorCode = BadRequest
                    //   ErrorDetails = ErrorDetails=Connection was closed by the remote host. Error code: 1007. Error details: Quota exceeded.
                    if (e.Reason == CancellationReason.Error)
                    {
                        Console.WriteLine($"CANCELED: ErrorCode={e.ErrorCode}");
                        Console.WriteLine($"CANCELED: ErrorDetails={e.ErrorDetails}");
                    }

                    stopRecognition.TrySetResult(0);
                };

                speechRecognizer.SessionStarted += (s, e) =>
                {
                    Console.WriteLine($"==========================");
                    Console.WriteLine($"    Session started event.");
                    Console.WriteLine($"==========================");
                    Console.WriteLine("\n\n");
                };

                speechRecognizer.SessionStopped += (s, e) =>
                {
                    Console.WriteLine("\n\n");
                    Console.WriteLine($"==========================");
                    Console.WriteLine($"    Session stopped event.");
                    Console.WriteLine($"Stop recognition.");
                    stopRecognition.TrySetResult(0);
                };

                // Starts continuous recognition.
                await speechRecognizer.StartContinuousRecognitionAsync().ConfigureAwait(false);

                // Waits for completion.
                // Use Task.WaitAny to keep the task rooted.
                Task.WaitAny(new[] { stopRecognition.Task });

                // Stops recognition.
                await speechRecognizer.StopContinuousRecognitionAsync().ConfigureAwait(false);
            }
            else
            {
                // Azure translation mode.
                var translationRecognizer = new TranslationRecognizer((SpeechTranslationConfig)speechConfig, audioInput);

                // Subscribing to the miscellaneous events.
                translationRecognizer.Recognizing += (s, e) =>
                {
                    // Recognizing events give a partial result (as compared to Recognized events)
                    Console.WriteLine($"      RECOGNIZING: Text= {e.Result.Text}");
                };

                translationRecognizer.Recognized += (s, e) =>
                {
                    if (e.Result.Reason == ResultReason.TranslatedSpeech)
                    {
                        Console.WriteLine($"--- RECOGNIZED: Text={e.Result.Text}");
                        foreach (var element in e.Result.Translations)
                        {
                            Console.WriteLine($"------ TRANSLATED into '{element.Key}': {element.Value}");
                        }
                        Console.WriteLine("");
                    }
                    else if (e.Result.Reason == ResultReason.RecognizedSpeech)
                    {
                        // Usually this means the specified target language code is invalid, like "zz-JP".  TranslationConfig
                        //     seems capable of some error corrections.  For instance, specifying "es-JP", it will process as
                        //     if "es" is specified.  "zh-TW" is processed as if "zh-Hant" is specified.
                        Console.WriteLine($"--- RECOGNIZED: Recognized but not translated.");
                    }
                    else if (e.Result.Reason == ResultReason.NoMatch)
                    {
                        // This might suggest a part of the audio which is not speech -- it can be noise or music.  Sometimes
                        //     audio spoken in a language different from the specified source language can also hit here.
                        Console.WriteLine($"--- RECOGNIZED NOMATCH: Speech could not be recognized and Text={e.Result.Text}.");
                    }
                    else
                    {
                        Console.WriteLine($"--- RECOGNIZED UNLISTED REASON: Text={e.Result.Text}.");
                    }
                };

                translationRecognizer.Canceled += (s, e) =>
                {
                    Console.WriteLine($"CANCELED: Reason={e.Reason}");

                    // Please read the comments on the Canceled event for speechRecognizer part.  Here
                    //     we only provide comments specific to translation.
                    //
                    // * If the specified language is invalid (say, "en-JP"), we have
                    //       ErrorCode = BadRequest
                    //       ErrorDetails = Connection was closed by the remote host. Error code: 1007. Error details: Invalid
                    //                      'language' query parameter or unknown custom deployment ......
                    if (e.Reason == CancellationReason.Error)
                    {
                        Console.WriteLine($"CANCELED: ErrorCode={e.ErrorCode}");
                        Console.WriteLine($"CANCELED: ErrorDetails={e.ErrorDetails}");
                    }

                    stopRecognition.TrySetResult(0);
                };

                translationRecognizer.SessionStarted += (s, e) =>
                {
                    Console.WriteLine($"==========================");
                    Console.WriteLine($"    Session started event.");
                    Console.WriteLine($"==========================");
                    Console.WriteLine("\n\n");
                };

                translationRecognizer.SessionStopped += (s, e) =>
                {
                    Console.WriteLine("\n\n");
                    Console.WriteLine($"==========================");
                    Console.WriteLine($"    Session stopped event.");
                    Console.WriteLine($"Stop recognition.");
                    stopRecognition.TrySetResult(0);
                };

                // Starts continuous recognition.
                await translationRecognizer.StartContinuousRecognitionAsync().ConfigureAwait(false);

                // Waits for completion.
                // Use Task.WaitAny to keep the task rooted.
                Task.WaitAny(new[] { stopRecognition.Task });

                // Stops recognition.
                await translationRecognizer.StopContinuousRecognitionAsync().ConfigureAwait(false);
            }
        }
    }
}
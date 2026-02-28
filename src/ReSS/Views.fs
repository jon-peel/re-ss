module ReSS.Views

open Giraffe.ViewEngine

type FormState =
    | Empty
    | Success of generatedUrl: string * unlockedCount: int * totalCount: int
    | FormError of sourceUrl: string * perDay: string * errors: Map<string, string>

let private layout (pageTitle: string) (pageBody: XmlNode list) =
    html [] [
        head [] [
            meta [ _charset "utf-8" ]
            meta [ _name "viewport"; _content "width=device-width, initial-scale=1" ]
            title [] [ str pageTitle ]
            tag "style" [] [ rawText """
body { font-family: system-ui, sans-serif; max-width: 640px; margin: 2rem auto; padding: 0 1rem; }
label { display: block; margin-top: 1rem; font-weight: 600; }
input[type=text], input[type=number], input[type=date] {
  width: 100%; padding: .5rem; margin-top: .25rem; box-sizing: border-box;
  border: 1px solid #ccc; border-radius: 4px;
}
.error { color: #c00; font-size: .875rem; margin-top: .25rem; }
.form-error { background: #fee; border: 1px solid #c00; padding: .75rem 1rem; border-radius: 4px; margin-bottom: 1rem; }
button[type=submit] {
  margin-top: 1.5rem; padding: .6rem 1.4rem; background: #0066cc; color: #fff;
  border: none; border-radius: 4px; cursor: pointer; font-size: 1rem;
}
button[type=submit]:hover { background: #0055aa; }
.result { background: #e8f5e9; border: 1px solid #388e3c; padding: 1rem; border-radius: 4px; margin-top: 1.5rem; }
.result code { word-break: break-all; display: block; margin-top: .5rem; font-size: .9rem; }
.copy-btn {
  margin-top: .5rem; padding: .35rem .85rem; background: #fff; color: #388e3c;
  border: 1px solid #388e3c; border-radius: 4px; cursor: pointer; font-size: .875rem;
}
.copy-btn:hover { background: #e8f5e9; }
details { margin-top: 1rem; }
summary { cursor: pointer; font-weight: 600; }
""" ]
        ]
        body [] pageBody
    ]

let formView (state: FormState) =
    let (sourceVal, perDayVal, errors, formError) =
        match state with
        | Empty -> ("", "3", Map.empty, None)
        | FormError (s, p, errs) ->
            let fe =
                errs |> Map.tryFind "form"
            (s, p, errs, fe)
        | Success _ -> ("", "3", Map.empty, None)

    let fieldError key =
        match Map.tryFind key errors with
        | Some msg -> p [ _class "error" ] [ str msg ]
        | None     -> emptyText

    let resultSection =
        match state with
        | Success (url, n, t) ->
            div [ _class "result" ] [
                p [] [ str (sprintf "Your new feed has %d of %d articles ready" n t) ]
                p [] [ str "Your personalised feed URL:" ]
                code [] [ str url ]
                button [
                    _type "button"
                    _class "copy-btn"
                    attr "onclick" (sprintf "navigator.clipboard.writeText('%s')" url)
                ] [ str "Copy" ]
            ]
        | _ -> emptyText

    layout "RSS Catch-Up Feed Generator" [
        h1 [] [ str "RSS Catch-Up Feed Generator" ]
        p [] [ str "Subscribe to an RSS feed at a comfortable pace." ]

        (match formError with
         | Some msg -> div [ _class "form-error" ] [ str msg ]
         | None -> emptyText)

        form [ _method "post"; _action "/" ] [
            label [] [ str "RSS Feed URL" ]
            input [ _type "text"; _name "sourceUrl"; _value sourceVal; _placeholder "https://example.com/feed" ]
            fieldError "sourceUrl"

            label [] [ str "Articles per day" ]
            input [ _type "number"; _name "perDay"; _value perDayVal; _min "1"; _max "1000" ]
            fieldError "perDay"

            details [] [
                summary [] [ str "Advanced options" ]
                label [] [ str "Start date" ]
                input [ _type "date"; _name "startDate" ]
                fieldError "startDate"
            ]

            button [ _type "submit" ] [ str "Generate feed URL" ]
        ]

        resultSection
    ]

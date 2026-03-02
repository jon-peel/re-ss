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
            link [ _rel "stylesheet"; _href "/app.css" ]
            script [ _src "/htmx.min.js" ] []
        ]
        body [] (pageBody @ [
            script [] [ rawText """
  document.querySelectorAll('.copy-btn[data-copy-url]').forEach(function(b) {
    b.addEventListener('click', function() {
      navigator.clipboard.writeText(b.getAttribute('data-copy-url'));
    });
  });
""" ]
        ])
    ]

let resultPartial (url: string) (n: int) (t: int) =
    div [ _id "result"; _class "result" ] [
        p [ _class "result-header" ] [ str (sprintf "✓  Feed ready — %d of %d articles today" n t) ]
        p [ _class "result-label" ] [ str "Your personalised feed URL" ]
        code [ _class "result-url" ] [ str url ]
        button [ _type "button"; _class "copy-btn"; attr "data-copy-url" url ] [ str "Copy URL" ]
    ]

let formCardPartial (state: FormState) =
    let (sourceVal, perDayVal, errors, formError) =
        match state with
        | Empty -> ("", "3", Map.empty, None)
        | FormError (s, p, errs) ->
            let fe = errs |> Map.tryFind "form"
            (s, p, errs, fe)
        | Success _ -> ("", "3", Map.empty, None)

    let fieldError key =
        match Map.tryFind key errors with
        | Some msg -> p [ _class "error" ] [ str msg ]
        | None     -> emptyText

    div [ _id "form-card" ] [
        (match formError with
         | Some msg -> div [ _class "form-error" ] [ str msg ]
         | None -> emptyText)

        div [ _class "card" ] [
            form [
                _method "post"
                _action "/"
                attr "hx-post" "/"
                attr "hx-target" "#result"
                attr "hx-swap" "outerHTML"
            ] [
                label [ _class "field-label" ] [ str "RSS Feed URL" ]
                input [ _type "text"; _name "sourceUrl"; _value sourceVal; _placeholder "https://example.com/feed" ]
                fieldError "sourceUrl"

                label [ _class "field-label" ] [ str "Articles per day" ]
                input [ _type "number"; _name "perDay"; _value perDayVal; _min "1"; _max "1000"; _class "input-narrow" ]
                fieldError "perDay"

                details [] [
                    summary [] [ str "Advanced options" ]
                    label [ _class "field-label" ] [ str "Start date" ]
                    input [ _type "date"; _name "startDate" ]
                    fieldError "startDate"
                ]

                button [ _type "submit" ] [ str "Generate feed URL →" ]
            ]
        ]
    ]

let formView (state: FormState) =
    let resultSection =
        match state with
        | Success (url, n, t) -> resultPartial url n t
        | _ -> div [ _id "result" ] []

    layout "RSS Catch-Up" [
        h1 [] [ str "RSS Catch-Up" ]
        p [ _class "subtitle" ] [ str "Subscribe to an RSS feed at your own pace." ]
        formCardPartial state
        resultSection
    ]

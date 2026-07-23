{{/*
Resource name — the ak-<service> identity. Required; fail fast if unset so a
misconfigured values file cannot render nameless objects.
*/}}
{{- define "antkart-service.name" -}}
{{- required "values.name (e.g. ak-products) is required" .Values.name -}}
{{- end -}}

{{/*
Fully-qualified container image, derived from the registry + serviceName so the
ACR path (antkart/<serviceName>) is never repeated by hand.
*/}}
{{- define "antkart-service.image" -}}
{{- $svc := required "values.serviceName (e.g. products) is required" .Values.serviceName -}}
{{- printf "%s/antkart/%s:%s" .Values.image.registry $svc .Values.image.tag -}}
{{- end -}}

{{/*
Common labels.
*/}}
{{- define "antkart-service.labels" -}}
app.kubernetes.io/name: {{ include "antkart-service.name" . }}
app.kubernetes.io/part-of: antkart
app.kubernetes.io/component: {{ .Values.serviceName | quote }}
app.kubernetes.io/managed-by: {{ .Release.Service }}
helm.sh/chart: {{ printf "%s-%s" .Chart.Name .Chart.Version }}
{{- end -}}

{{/*
Selector labels (stable — must not include version/tag).
*/}}
{{- define "antkart-service.selectorLabels" -}}
app.kubernetes.io/name: {{ include "antkart-service.name" . }}
{{- end -}}

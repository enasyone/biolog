import { PlaceholderPage } from '../../../components/placeholder-page';

// BIO-INT-002-AC06 HYBRID disclosure: this route shares the IntegrationService port with
// /administration/integrations, but its own primary actions ("Save authentication policy",
// "Test sign-in") are BIO-INT-001's own auth-provider/SSO domain, not this story's SIS/HR/ERP
// import framework. BIO-INT-002 does not reach into BIO-INT-001's scope to wire this page live -
// it stays SIMULATION/mock here, a deliberate, disclosed boundary (AC06 explicitly permits this),
// not a gap this story left behind. See docs/features/bio-int-002/README.md.
export default function SettingsAuthenticationPage() {
  return (
    <PlaceholderPage
      title="Authentication Settings"
      module="IntegrationService"
      description="Sign-in method and session policy configuration."
    />
  );
}
